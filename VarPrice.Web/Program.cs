using Polly;
using Polly.Extensions.Http;
using Serilog;
using VarPrice.Web.Crawler;
using VarPrice.Web.Logging;
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

builder.Services
    .AddHttpClient<IVarusHttpClient, VarusHttpClient>((sp, http) =>
    {
        var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CrawlerOptions>>().Value;
        http.Timeout = opt.HttpTimeout;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("VarPriceBot/0.2 (+contact: you)");
    })
    .AddPolicyHandler((sp, _) =>
    {
        var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CrawlerOptions>>().Value;
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(
                Math.Max(1, opt.HttpRetryCount),
                attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)),
                (outcome, delay, attempt, context) =>
                {
                    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("VarusHttpRetryPolicy");
                    logger.LogWarning(
                        outcome.Exception,
                        "Retry {Attempt} after {DelayMs}ms due to {Reason}",
                        attempt,
                        delay.TotalMilliseconds,
                        outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                });
    });

builder.Services.AddSingleton<ILoggingBootstrapper, LoggingBootstrapper>();
builder.Services.AddSingleton<IPgConnectionFactory, PgConnectionFactory>();
builder.Services.AddScoped<SchemaBootstrapper>();
builder.Services.AddScoped<ICrawlerRepository, PgCrawlerRepository>();

builder.Services.AddSingleton<ISitemapParser, SitemapParser>();
builder.Services.AddScoped<ISitemapCrawler, SitemapCrawler>();
builder.Services.AddSingleton<IProductUrlFilter, VarusProductUrlFilter>();
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
app.Logger.LogInformation("Application starting in MCP-3");

app.Run();
