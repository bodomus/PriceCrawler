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

var builder = Host.CreateApplicationBuilder(args);
var executableDirectoryPath = AppContext.BaseDirectory;
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

var once = args.Contains("--once");
var jobIndex = Array.IndexOf(args, "--job");
var job = jobIndex >= 0 && jobIndex + 1 < args.Length ? args[jobIndex + 1] : "vegetables";

if (!string.Equals(job, "vegetables", StringComparison.OrdinalIgnoreCase))
{
    logger.LogError("Unsupported job: {Job}", job);
    return 2;
}

using var runScope = host.Services.CreateScope();
var useCase = runScope.ServiceProvider.GetRequiredService<IRunCrawlerUseCase>();
var progressState = runScope.ServiceProvider.GetRequiredService<CrawlerProgressState>();
var dashboard = new CrawlerConsoleDashboard(progressState, TimeSpan.FromMilliseconds(200));
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

CrawlerRunResult result;
dashboard.Start();
try
{
    result = await useCase.RunVegetablesAsync(cancellation.Token);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    logger.LogWarning("Crawler run was cancelled.");
    return 1;
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

if (once)
{
    return string.Equals(result.Status, "ok", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
}

return string.Equals(result.Status, "ok", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
