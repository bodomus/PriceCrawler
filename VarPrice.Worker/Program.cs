using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using VarPrice.Application.DependencyInjection;
using VarPrice.Application.UseCases;
using VarPrice.Infrastructure.DependencyInjection;
using VarPrice.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddVarPriceApplication(builder.Configuration);
builder.Services.AddVarPriceInfrastructure(builder.Configuration);

builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

using var host = builder.Build();

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
    Console.Error.WriteLine($"Unsupported job: {job}");
    return 2;
}

using var runScope = host.Services.CreateScope();
var useCase = runScope.ServiceProvider.GetRequiredService<RunCrawlerUseCase>();
var result = await useCase.RunVegetablesAsync(CancellationToken.None);

Console.WriteLine($"run_id={result.RunId}; status={result.Status}; processed={result.ProductsProcessed}; errors={result.Errors}");

if (once)
{
    return string.Equals(result.Status, "ok", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
}

return string.Equals(result.Status, "ok", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
