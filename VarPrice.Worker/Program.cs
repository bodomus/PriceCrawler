using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;

using VarPrice.Application.Abstractions;
using VarPrice.Application.DependencyInjection;
using VarPrice.Infrastructure.DependencyInjection;
using VarPrice.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);
var executableDirectoryPath = AppContext.BaseDirectory;
var logsDirectoryPath = Path.Combine(executableDirectoryPath, "logs");
Directory.CreateDirectory(logsDirectoryPath);
var logFilePath = Path.Combine(logsDirectoryPath, "varprice-worker.log");

builder.Services.AddVarPriceApplication(builder.Configuration);
builder.Services.AddVarPriceInfrastructure(builder.Configuration);
builder.Services.AddUrlFilterOptionsFromFile(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddCategorySeedUrlFileOptions(builder.Configuration, builder.Environment.ContentRootPath);

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
        shared: true));

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
var result = await useCase.RunVegetablesAsync(CancellationToken.None);

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
