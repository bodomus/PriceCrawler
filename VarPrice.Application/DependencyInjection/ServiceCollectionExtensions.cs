using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VarPrice.Application.Models;
using VarPrice.Application.UseCases;

namespace VarPrice.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVarPriceApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CrawlerOptions>(configuration.GetSection("Crawler"));
        services.AddScoped<RunCrawlerUseCase>();
        return services;
    }
}
