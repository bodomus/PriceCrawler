using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PriceCrawler.Application.Abstractions;
using PriceCrawler.Application.Features.Products;
using PriceCrawler.Application.Models;
using PriceCrawler.Domain.Interfaces;
using PriceCrawler.Infrastructure.Crawler;
using PriceCrawler.Infrastructure.Persistence;
using PriceCrawler.Infrastructure.Queries.Products;

namespace PriceCrawler.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers everything the crawler (Worker) and the legacy site need: persistence and the Varus crawler.</summary>
    /// <param name="services">Service collection to add to.</param>
    /// <param name="configuration">Configuration with the <c>Postgres</c> connection string and crawler options.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddPriceCrawlerInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPriceCrawlerPersistence(configuration);

        services.AddHttpClient("varus", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("PriceCrawlerBot/0.1 (+contact: you)");
        });


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
        services.AddScoped<ICategoryTreeSource, VarusCategoryTreeSource>();
        services.AddScoped<IVarusCategoryMenuParser, VarusCategoryMenuParser>();
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

    /// <summary>
    /// Registers only the PostgreSQL persistence: the DbContext, the connection factory, schema
    /// startup checks and the repositories that need no crawler settings. The Web API uses this
    /// without the crawler.
    /// </summary>
    /// <param name="services">Service collection to add to.</param>
    /// <param name="configuration">Configuration with the <c>Postgres</c> connection string.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="InvalidOperationException">The <c>Postgres</c> connection string is missing.</exception>
    public static IServiceCollection AddPriceCrawlerPersistence(this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
                               ?? throw new InvalidOperationException(
                                   "Connection string 'Postgres' is not configured.");

        services.AddDbContext<PriceCrawlerDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<IPgConnectionFactory, PgConnectionFactory>();
        services.AddScoped<SchemaBootstrapper>();
        services.AddScoped<DatabaseSchemaInitializer>();
        services.AddScoped<DatabaseSchemaVersionReader>();
        services.AddScoped<DatabaseSchemaValidator>();
        services.AddScoped<DatabaseSchemaStartupCoordinator>();
        services.AddOptions<DatabaseSchemaOptions>()
            .Bind(configuration.GetSection(DatabaseSchemaOptions.SectionName))
            .Validate(
                value => Enum.IsDefined(value.StartupMode),
                $"{DatabaseSchemaOptions.SectionName}:StartupMode is invalid.")
            .ValidateOnStart();
        services.AddScoped<PgRoutineExecutor>();

        services.AddScoped<ICategoryRepository, PgCategoryRepository>();
        services.AddScoped<IProductListReader, PgProductListReader>();
        return services;
    }
}
