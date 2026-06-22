using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;

using VarPrice.Application.Abstractions;
using VarPrice.Application.DependencyInjection;
using VarPrice.Application.Models;
using VarPrice.Infrastructure.DependencyInjection;
using VarPrice.Infrastructure.Persistence;
using VarPrice.Worker;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

var commandResult = WorkerCommandParser.Parse(args);
if (!commandResult.IsValid)
{
    Console.Error.WriteLine(commandResult.ErrorMessage);
    Console.Error.WriteLine();
    Console.Error.WriteLine(WorkerCommandParser.GetHelpText());
    return WorkerCommandParser.InvalidCommandExitCode;
}

if (commandResult.ShowHelp)
{
    Console.WriteLine(WorkerCommandParser.GetHelpText());
    return WorkerCommandParser.SuccessExitCode;
}

var command = commandResult.Command ?? throw new InvalidOperationException("Worker command was not resolved.");

var executableDirectoryPath = AppContext.BaseDirectory;
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = [],
    ContentRootPath = executableDirectoryPath
});
var logsDirectoryPath = Path.Combine(executableDirectoryPath, "logs");
Directory.CreateDirectory(logsDirectoryPath);
var logFilePath = Path.Combine(logsDirectoryPath, "varprice-worker.log");
var logFileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

builder.Services.AddVarPriceApplication(builder.Configuration);
builder.Services.AddVarPriceInfrastructure(builder.Configuration);
builder.Services.AddUrlFilterOptionsFromFile(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddCategorySeedUrlFileOptions(
    builder.Configuration,
    executableDirectoryPath,
    builder.Environment.ContentRootPath);
builder.Services.AddSingleton<CrawlerProgressState>();
builder.Services.AddSingleton<ICrawlerProgressReporter>(provider =>
    provider.GetRequiredService<CrawlerProgressState>());

builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.File(
        logFilePath,
        rollingInterval: RollingInterval.Infinite,
        fileSizeLimitBytes: 1 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        retainedFileCountLimit: 10,
        shared: true,
        encoding: logFileEncoding));

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("VarPrice.Worker");

using (var scope = host.Services.CreateScope())
{
    var bootstrap = scope.ServiceProvider.GetRequiredService<SchemaBootstrapper>();
    await bootstrap.EnsureSchemaAsync();
}

using var runScope = host.Services.CreateScope();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

if (command.Mode == WorkerRunMode.CatalogRefresh)
{
    var refreshUseCase = runScope.ServiceProvider.GetRequiredService<IRefreshProductCatalogUseCase>();
    try
    {
        var refreshResult = await refreshUseCase.ExecuteAsync(cancellation.Token);
        logger.LogInformation(
            "catalog_refresh run_id={RunId}; status={Status}; source={Source}; discovered={Discovered}; accepted={Accepted}; inserted={Inserted}; updated={Updated}; skipped={Skipped}",
            refreshResult.RunId,
            refreshResult.Status,
            refreshResult.Source,
            refreshResult.DiscoveredCount,
            refreshResult.AcceptedCount,
            refreshResult.InsertedCount,
            refreshResult.UpdatedCount,
            refreshResult.SkippedCount);
        return refreshResult.Status == RefreshProductCatalogStatus.Ok
            ? WorkerCommandParser.SuccessExitCode
            : WorkerCommandParser.FailedRunExitCode;
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        logger.LogWarning("Catalog refresh was cancelled.");
        return WorkerCommandParser.FailedRunExitCode;
    }
}

if (command.Mode == WorkerRunMode.CollectPrices)
{
    var collectUseCase = runScope.ServiceProvider.GetRequiredService<ICollectProductPricesUseCase>();
    try
    {
        var collectResult = await collectUseCase.ExecuteAsync(cancellation.Token);
        logger.LogInformation(
            "collect_prices run_id={RunId}; status={Status}; selected={Selected}; enqueued={Enqueued}; succeeded={Succeeded}; retry={Retry}; dead={Dead}",
            collectResult.RunId,
            collectResult.Status,
            collectResult.SelectedCount,
            collectResult.EnqueuedCount,
            collectResult.SucceededCount,
            collectResult.RetryCount,
            collectResult.DeadCount);
        return string.Equals(collectResult.Status, "ok", StringComparison.OrdinalIgnoreCase)
            ? WorkerCommandParser.SuccessExitCode
            : WorkerCommandParser.FailedRunExitCode;
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        logger.LogWarning("Price collection was cancelled.");
        return WorkerCommandParser.FailedRunExitCode;
    }
}

var useCase = runScope.ServiceProvider.GetRequiredService<IRunCrawlerUseCase>();
var progressState = runScope.ServiceProvider.GetRequiredService<CrawlerProgressState>();
var dashboard = new CrawlerConsoleDashboard(progressState, TimeSpan.FromMilliseconds(200));
CrawlerRunResult result;
dashboard.Start();
try
{
    result = await useCase.RunVegetablesAsync(cancellation.Token);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    logger.LogWarning("Crawler run was cancelled.");
    return WorkerCommandParser.FailedRunExitCode;
}
finally
{
    await dashboard.StopAsync();
}

logger.LogInformation(
    "run_id={RunId}; status={Status}; processed={Processed}; errors={Errors}",
    result.RunId,
    result.Status,
    result.ProductsProcessed,
    result.Errors);

return string.Equals(result.Status, "ok", StringComparison.OrdinalIgnoreCase)
    ? WorkerCommandParser.SuccessExitCode
    : WorkerCommandParser.FailedRunExitCode;
