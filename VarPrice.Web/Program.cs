using Microsoft.EntityFrameworkCore;

using Serilog;
using Serilog.Context;

using VarPrice.Application.DependencyInjection;
using VarPrice.Application.Grids.Runs;
using VarPrice.Infrastructure.DependencyInjection;
using VarPrice.Infrastructure.Persistence;
using VarPrice.Web.Logging;

using InfrastructureRuns = VarPrice.Infrastructure.Queries.Runs;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName());

builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<VarPriceDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Postgres")
                           ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    options.UseNpgsql(connectionString);
});
builder.Services.AddScoped<IRunsGridQuerySource, InfrastructureRuns.RunsGridQuerySource>();
builder.Services.AddScoped<ISnapshotsGridQuerySource, InfrastructureRuns.SnapshotsGridQuerySource>();
builder.Services.AddScoped<IProductsGridQuerySource, InfrastructureRuns.ProductsGridQuerySource>();

builder.Services.AddVarPriceApplication(builder.Configuration);
builder.Services.AddVarPriceInfrastructure(builder.Configuration);
builder.Services.AddUrlFilterOptionsFromFile(builder.Configuration, builder.Environment.ContentRootPath);

builder.Services.AddSingleton<ILoggingBootstrapper, LoggingBootstrapper>();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseStaticFiles();
app.Use(async (context, next) =>
{
    using (LogContext.PushProperty("CorrelationId", context.TraceIdentifier))
    {
        await next();
    }
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Runs}/{action=Index}/{id?}");
app.MapGet("/health", () => Results.Ok(new { ok = true }));

using (var scope = app.Services.CreateScope())
{
    var bootstrap = scope.ServiceProvider.GetRequiredService<SchemaBootstrapper>();
    await bootstrap.EnsureSchemaAsync();
}

app.Logger.LogInformation("Application starting in {EnvironmentName}", app.Environment.EnvironmentName);

app.Run();
