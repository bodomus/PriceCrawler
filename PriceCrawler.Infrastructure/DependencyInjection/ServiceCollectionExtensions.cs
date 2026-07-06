using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PriceCrawler.Application.Abstractions;
using PriceCrawler.Application.Models;
using PriceCrawler.Domain.Interfaces;
using PriceCrawler.Infrastructure.Crawler;
using PriceCrawler.Infrastructure.Persistence;

namespace PriceCrawler.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPriceCrawlerInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
                               ?? throw new InvalidOperationException(
                                   "Connection string 'Postgres' is not configured.");

        services.AddDbContext<PriceCrawlerDbContext>(options => options.UseNpgsql(connectionString));

        services.AddHttpClient("varus", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("PriceCrawlerBot/0.1 (+contact: you)");
        });

        services.AddSingleton<IPgConnectionFactory, PgConnectionFactory>();
        services.AddScoped<SchemaBootstrapper>();
        services.AddScoped<PgRoutineExecutor>();

        services.AddScoped<ICrawlerRunRepository, PgCrawlerRunRepository>();
        services.AddScoped<ICrawlerRunReadRepository, PgCrawlerRunReadRepository>();
        services.AddScoped<IIngestionRunRepository, PgIngestionRunRepository>();
        services.AddScoped<IPriceSnapshotRepository, PgPriceSnapshotRepository>();
        services.AddScoped<IPriceCollectQueueRepository, PgPriceCollectQueueRepository>();
        services.AddScoped<IProductCatalogRepository, PgProductCatalogRepository>();
        services.AddScoped<IProductCatalogRefreshRepository, PgProductCatalogRefreshRepository>();

        services.AddSingleton<ISitemapUrlProvider, SitemapUrlProvider>();
        services.AddSingleton<ISitemapHttpClient, SitemapHttpClient>();
        services.AddSingleton<ISitemapResponseValidator, SitemapResponseValidator>();
        services.AddScoped<SitemapDiscoveryService>();
        services.AddScoped<IProductUrlSource, SitemapReader>();
        services.AddScoped<ICategorySeedProvider, CategorySeedProvider>();
        services.AddScoped<ICategoryPageLoader, CategoryPageLoader>();
        services.AddScoped<ICategoryProductLinkExtractor, CategoryProductLinkExtractor>();
        services.AddScoped<ICategoryPaginationStrategy, CategoryPaginationStrategy>();
        services.AddScoped<IProductUrlDiscoveryStrategyFactory, ProductUrlDiscoveryStrategyFactory>();
        services.AddScoped<CategorySeedProductUrlDiscoveryStrategy>();
        services.AddScoped<ICategoryProductUrlDiscoverySource, CategoryProductUrlDiscoverySource>();
        services.AddScoped<IListingPageExtractor, VarusListingPageExtractor>();
        services.AddSingleton<VarusRequestCoordinator>();
        services.AddScoped<VarusProductCardExtractor>();
        services.AddScoped<StubProductCardExtractor>();
        services.AddScoped<IProductCardExtractor>(provider =>
        {
            var crawlerOptions = provider.GetRequiredService<IOptions<CrawlerOptions>>().Value;
            return crawlerOptions.UseStubProductCardExtractor
                ? provider.GetRequiredService<StubProductCardExtractor>()
                : provider.GetRequiredService<VarusProductCardExtractor>();
        });

        return services;
    }
}
