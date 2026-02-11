using Serilog;
using VarPrice.Web.Crawler;
using VarPrice.Web.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName());

builder.Services.AddRazorPages();

builder.Services.Configure<CrawlerOptions>(builder.Configuration.GetSection("Crawler"));

builder.Services.AddHttpClient("varus", c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("VarPriceBot/0.1 (+contact: you)");
});

builder.Services.AddSingleton<IPgConnectionFactory, PgConnectionFactory>();
builder.Services.AddScoped<SchemaBootstrapper>();
builder.Services.AddScoped<ICrawlerRepository, PgCrawlerRepository>();

builder.Services.AddScoped<ISitemapReader, SitemapReader>();
builder.Services.AddScoped<IProductCardExtractor, VarusProductCardExtractor>();
builder.Services.AddScoped<CrawlerRunner>();

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
