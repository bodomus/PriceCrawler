using System.Text;

using Microsoft.EntityFrameworkCore;

using Serilog;
using Serilog.Context;

using PriceCrawler.Application.DependencyInjection;
using PriceCrawler.Application.Grids.Runs;
using PriceCrawler.Infrastructure.DependencyInjection;
using PriceCrawler.Infrastructure.Persistence;
using PriceCrawler.Web.Logging;

using InfrastructureRuns = PriceCrawler.Infrastructure.Queries.Runs;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName());

builder.Services.AddControllersWithViews();
builder.Services.AddKendo();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<PriceCrawlerDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Postgres")
                           ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    options.UseNpgsql(connectionString);
});
builder.Services.AddScoped<IRunsGridQuerySource, InfrastructureRuns.RunsGridQuerySource>();
builder.Services.AddScoped<IRunsTreeQuerySource, InfrastructureRuns.RunsTreeQuerySource>();
builder.Services.AddScoped<ISnapshotsGridQuerySource, InfrastructureRuns.SnapshotsGridQuerySource>();
builder.Services.AddScoped<IProductsGridQuerySource, InfrastructureRuns.ProductsGridQuerySource>();
builder.Services.AddScoped<IProductDetailsQuerySource, InfrastructureRuns.ProductDetailsQuerySource>();
builder.Services.AddScoped<IProductPriceHistoryQuerySource, InfrastructureRuns.ProductPriceHistoryQuerySource>();
builder.Services.AddScoped<IProductAnalysisService, InfrastructureRuns.ProductAnalysisService>();

builder.Services.AddPriceCrawlerApplication(builder.Configuration);
builder.Services.AddPriceCrawlerInfrastructure(builder.Configuration);
builder.Services.AddUrlFilterOptionsFromFile(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddCategorySeedUrlFileOptions(
    builder.Configuration,
    AppContext.BaseDirectory,
    builder.Environment.ContentRootPath);

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
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { ok = true }));

using (var scope = app.Services.CreateScope())
{
    var databaseStartup = scope.ServiceProvider.GetRequiredService<DatabaseSchemaStartupService>();
    await databaseStartup.ValidateAndInitializeAsync(app.Environment.EnvironmentName);
}

app.Logger.LogInformation("Application starting in {EnvironmentName}", app.Environment.EnvironmentName);

app.Run();
