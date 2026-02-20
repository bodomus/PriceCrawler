using VarPrice.Web.Logging;
using Serilog;
using VarPrice.Application.DependencyInjection;
using VarPrice.Infrastructure.DependencyInjection;
using VarPrice.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName());

builder.Services.AddRazorPages();
builder.Services.AddVarPriceApplication(builder.Configuration);
builder.Services.AddVarPriceInfrastructure(builder.Configuration);

builder.Services.AddSingleton<ILoggingBootstrapper, LoggingBootstrapper>();

var app = builder.Build();

app.UseSerilogRequestLogging();

app.MapRazorPages();
app.MapGet("/health", () => Results.Ok(new { ok = true }));

using (var scope = app.Services.CreateScope())
{
    var bootstrap = scope.ServiceProvider.GetRequiredService<SchemaBootstrapper>();
    await bootstrap.EnsureSchemaAsync();
}

app.Logger.LogInformation("Application starting in {EnvironmentName}", app.Environment.EnvironmentName);

app.Run();
