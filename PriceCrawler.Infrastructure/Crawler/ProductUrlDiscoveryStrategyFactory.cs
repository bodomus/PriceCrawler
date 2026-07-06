using Microsoft.Extensions.Options;

using PriceCrawler.Application.Abstractions;
using PriceCrawler.Application.Models;
using PriceCrawler.Application.UseCases;

namespace PriceCrawler.Infrastructure.Crawler;

public sealed class ProductUrlDiscoveryStrategyFactory(
    IOptions<CrawlerOptions> options,
    CategorySeedProductUrlDiscoveryStrategy categorySeedStrategy,
    ApiProductUrlDiscoveryStrategy apiStrategy,
    SitemapProductUrlDiscoveryStrategy sitemapStrategy) : IProductUrlDiscoveryStrategyFactory
{
    public IProductUrlDiscoveryStrategy Create()
    {
        var mode = string.IsNullOrWhiteSpace(options.Value.DiscoveryMode)
            ? ProductUrlDiscoveryModes.CategorySeeds
            : options.Value.DiscoveryMode.Trim();

        return mode switch
        {
            _ when string.Equals(mode, ProductUrlDiscoveryModes.CategorySeeds, StringComparison.OrdinalIgnoreCase) =>
                categorySeedStrategy,
            _ when string.Equals(mode, ProductUrlDiscoveryModes.Api, StringComparison.OrdinalIgnoreCase) =>
                apiStrategy,
            _ when string.Equals(mode, ProductUrlDiscoveryModes.Sitemap, StringComparison.OrdinalIgnoreCase) =>
                sitemapStrategy,
            _ => throw new InvalidOperationException(
                $"Unsupported Crawler:DiscoveryMode '{mode}'. Supported values: CategorySeeds, Api, Sitemap.")
        };
    }
}
