using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Context;

using PriceCrawler.Application.Abstractions;
using PriceCrawler.Application.DependencyInjection;
using PriceCrawler.Application.Models;
using PriceCrawler.Infrastructure.DependencyInjection;
using PriceCrawler.Infrastructure.Persistence;
using PriceCrawler.Worker;

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
var executionId = Guid.NewGuid().ToString("N");
using var executionLogContext = LogContext.PushProperty("ExecutionId", executionId);

var executableDirectoryPath = AppContext.BaseDirectory;
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = [],
    ContentRootPath = executableDirectoryPath
});
var logsDirectoryPath = Path.Combine(executableDirectoryPath, "logs");
Directory.CreateDirectory(logsDirectoryPath);
var logFilePath = Path.Combine(logsDirectoryPath, "pricecrawler-worker.log");
var logFileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

builder.Services.AddPriceCrawlerApplication(builder.Configuration);
builder.Services.AddPriceCrawlerInfrastructure(builder.Configuration);
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
        encoding: logFileEncoding,
        outputTemplate:
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [ExecutionId={ExecutionId}] {Message:lj}{NewLine}{Exception}"));

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PriceCrawler.Worker");
logger.LogInformation(
    "Worker command started. ExecutionId={ExecutionId}; Mode={Mode}",
    executionId,
    command.Mode);

using (var scope = host.Services.CreateScope())
{
    var bootstrap = scope.ServiceProvider.GetRequiredService<SchemaBootstrapper>();
    await bootstrap.EnsureSchemaAsync();
}

using var runScope = host.Services.CreateScope();
var progressState = runScope.ServiceProvider.GetRequiredService<CrawlerProgressState>();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

if (command.Mode == WorkerRunMode.CatalogRefresh)
{
    return await RunWithDashboardAsync(async () =>
    {
        var refreshUseCase = runScope.ServiceProvider.GetRequiredService<IRefreshProductCatalogUseCase>();
        try
        {
            var refreshResult = await refreshUseCase.ExecuteAsync(cancellation.Token);
            PrintCatalogSummary(refreshResult);
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
    });
}

if (command.Mode == WorkerRunMode.CollectPrices)
{
    return await RunWithDashboardAsync(async () =>
    {
        var collectUseCase = runScope.ServiceProvider.GetRequiredService<ICollectProductPricesUseCase>();
        try
        {
            var collectResult = await collectUseCase.ExecuteAsync(cancellation.Token);
            PrintPriceSummary(collectResult);
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
    });
}

if (command.Mode == WorkerRunMode.RunAll)
{
    return await RunWithDashboardAsync(async () =>
    {
        try
        {
            Console.WriteLine($"ExecutionId: {executionId}");
            var refresh = await runScope.ServiceProvider.GetRequiredService<IRefreshProductCatalogUseCase>()
                .ExecuteAsync(cancellation.Token);
            Console.WriteLine("Catalog refresh:");
            PrintCatalogSummary(refresh, "  ");
            logger.LogInformation(
                "Run-all catalog refresh completed. ExecutionId={ExecutionId}; CatalogRunId={CatalogRunId}; CatalogStatus={CatalogStatus}",
                executionId,
                refresh.RunId,
                refresh.Status);
            if (refresh.Status != RefreshProductCatalogStatus.Ok) return WorkerCommandParser.FailedRunExitCode;

            var prices = await runScope.ServiceProvider.GetRequiredService<ICollectProductPricesUseCase>()
                .ExecuteAsync(cancellation.Token);
            Console.WriteLine("Price collection:");
            PrintPriceSummary(prices, "  ");
            logger.LogInformation(
                "Run-all completed. ExecutionId={ExecutionId}; CatalogRunId={CatalogRunId}; CatalogStatus={CatalogStatus}; PriceRunId={PriceRunId}; PriceStatus={PriceStatus}",
                executionId,
                refresh.RunId,
                refresh.Status,
                prices.RunId,
                prices.Status);
            return string.Equals(prices.Status, "ok", StringComparison.OrdinalIgnoreCase)
                ? WorkerCommandParser.SuccessExitCode
                : WorkerCommandParser.FailedRunExitCode;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            logger.LogWarning("Run-all was cancelled.");
            return WorkerCommandParser.FailedRunExitCode;
        }
    });
}

var useCase = runScope.ServiceProvider.GetRequiredService<IRunCrawlerUseCase>();
return await RunWithDashboardAsync(async () =>
{
    CrawlerRunResult result;
    try
    {
        result = await useCase.RunVegetablesAsync(cancellation.Token);
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        logger.LogWarning("Crawler run was cancelled.");
        return WorkerCommandParser.FailedRunExitCode;
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
});

async Task<int> RunWithDashboardAsync(Func<Task<int>> commandHandler)
{
    var dashboard = new CrawlerConsoleDashboard(progressState, TimeSpan.FromMilliseconds(200));
    dashboard.Start();
    if (!dashboard.IsEnabled)
    {
        var reason = CrawlerConsoleDashboard.GetDisabledReason();
        logger.LogInformation("Crawler dashboard disabled. Reason={Reason}", reason);
        if (!Console.IsOutputRedirected)
        {
            Console.WriteLine($"Dashboard disabled: {reason}");
        }
    }

    try
    {
        return await commandHandler();
    }
    finally
    {
        await dashboard.StopAsync();
    }
}

static void PrintCatalogSummary(RefreshProductCatalogResult result, string prefix = "")
{
    Console.WriteLine($"{prefix}Command: catalog-refresh");
    Console.WriteLine($"{prefix}Status: {result.Status.ToString().ToLowerInvariant()}");
    Console.WriteLine($"{prefix}RunId: {result.RunId}");
    Console.WriteLine($"{prefix}DurationMs: {result.DurationMs}");
    Console.WriteLine($"{prefix}Discovered: {result.DiscoveredCount}");
    Console.WriteLine($"{prefix}Accepted: {result.AcceptedCount}");
    Console.WriteLine($"{prefix}Inserted: {result.InsertedCount}");
    Console.WriteLine($"{prefix}Updated: {result.UpdatedCount}");
    Console.WriteLine($"{prefix}Reactivated: {result.ReactivatedCount}");
    Console.WriteLine($"{prefix}Deactivated: {result.DeactivatedCount}");
}

static void PrintPriceSummary(CollectProductPricesResult result, string prefix = "")
{
    Console.WriteLine($"{prefix}Command: collect-prices");
    Console.WriteLine($"{prefix}Status: {result.Status}");
    Console.WriteLine($"{prefix}RunId: {result.RunId}");
    Console.WriteLine($"{prefix}DurationMs: {result.DurationMs}");
    Console.WriteLine($"{prefix}Selected: {result.SelectedCount}");
    Console.WriteLine($"{prefix}Enqueued: {result.EnqueuedCount}");
    Console.WriteLine($"{prefix}Succeeded: {result.SucceededCount}");
    Console.WriteLine($"{prefix}Retry: {result.RetryCount}");
    Console.WriteLine($"{prefix}Dead: {result.DeadCount}");
    Console.WriteLine($"{prefix}Products created: {result.ProductsCreatedCount}");
    Console.WriteLine($"{prefix}Products updated: {result.ProductsUpdatedCount}");
    Console.WriteLine($"{prefix}Snapshots created: {result.SnapshotsCreatedCount}");
    Console.WriteLine($"{prefix}Errors created: {result.ErrorsCreatedCount}");
}
