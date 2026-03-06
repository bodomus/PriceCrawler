using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using VarPrice.Application.Abstractions;
using VarPrice.Domain.Interfaces;
using VarPrice.Infrastructure.Crawler;
using VarPrice.Infrastructure.Persistence;

namespace VarPrice.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVarPriceInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpClient("varus", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("VarPriceBot/0.1 (+contact: you)");
        });

        services.AddSingleton<IPgConnectionFactory, PgConnectionFactory>();
        services.AddScoped<SchemaBootstrapper>();

        services.AddScoped<ICrawlerRunRepository, PgCrawlerRunRepository>();
        services.AddScoped<IIngestionRunRepository, PgIngestionRunRepository>();
        services.AddScoped<IPriceSnapshotRepository, PgPriceSnapshotRepository>();

        services.AddScoped<IProductUrlSource, SitemapReader>();
        services.AddSingleton<VarusRequestCoordinator>();
        services.AddScoped<IProductCardExtractor, VarusProductCardExtractor>();

        return services;
    }
}
