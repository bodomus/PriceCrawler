using Microsoft.EntityFrameworkCore;

using Serilog;
using Serilog.Context;

using VarPrice.Application.Abstractions;
using VarPrice.Application.DependencyInjection;
using VarPrice.Application.Grids;
using VarPrice.Application.Models;
using VarPrice.Infrastructure.Crawler;
using VarPrice.Infrastructure.Persistence;
using VarPrice.Web.Crawler;
using VarPrice.Web.Logging;
using VarPrice.Web.Storage;
using VarPrice.Web.Storage.Db;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName());

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<VarPriceDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Postgres")
                           ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    options.UseNpgsql(connectionString);
});
builder.Services.AddScoped<IDataTableRequestParser, DataTableRequestParser>();
builder.Services.AddScoped<IDataTableQueryService, DataTableQueryService>();

builder.Services.Configure<CrawlerOptions>(builder.Configuration.GetSection("Crawler"));
builder.Services.AddUrlFilterOptionsFromFile(builder.Configuration, builder.Environment.ContentRootPath);

builder.Services.AddHttpClient("varus", c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("VarPriceBot/0.1 (+contact: you)");
});

builder.Services.AddSingleton<ILoggingBootstrapper, LoggingBootstrapper>();
builder.Services.AddSingleton<IPgConnectionFactory, PgConnectionFactory>();
builder.Services.AddSingleton<DbErrorMapper>();
builder.Services.AddScoped<DbExecutor>();
builder.Services.AddScoped<SchemaBootstrapper>();
builder.Services.AddScoped<ICrawlerRepository, PgCrawlerRepository>();

builder.Services.AddScoped<IProductUrlSource, SitemapReader>();
builder.Services.AddSingleton<VarusRequestCoordinator>();
builder.Services.AddScoped<IProductCardExtractor, VarusProductCardExtractor>();
builder.Services.AddScoped<ICrawlerRunner, CrawlerRunner>();

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

app.MapRazorPages();
app.MapGet("/health", () => Results.Ok(new { ok = true }));

using (var scope = app.Services.CreateScope())
{
    var bootstrap = scope.ServiceProvider.GetRequiredService<SchemaBootstrapper>();
    await bootstrap.EnsureSchemaAsync();
}

app.Logger.LogInformation("Application starting in {EnvironmentName}", app.Environment.EnvironmentName);

app.Run();
