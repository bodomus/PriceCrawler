using Microsoft.Extensions.DependencyInjection;

using PriceCrawler.Mediator.DependencyInjection;

namespace PriceCrawler.Application.Features;

/// <summary>Registers the CQRS features (queries, commands and their handlers) of the application layer.</summary>
public static class FeaturesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the mediator with every handler and validator of this assembly and the standard
    /// pipeline: logging (outermost), then validation.
    /// </summary>
    /// <param name="services">Service collection to add to.</param>
    /// <returns>The mediator builder, to add more behaviors.</returns>
    public static MediatorBuilder AddPriceCrawlerFeatures(this IServiceCollection services) =>
        services
            .AddMediator(typeof(FeaturesServiceCollectionExtensions).Assembly)
            .AddLogging()
            .AddValidation();
}
