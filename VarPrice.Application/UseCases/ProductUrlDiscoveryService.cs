using Microsoft.Extensions.Logging;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;

namespace VarPrice.Application.UseCases;

public sealed class ProductUrlDiscoveryService(
    ISitemapProductUrlDiscoverySource sitemapSource,
    ICategoryProductUrlDiscoverySource categorySource,
    IProductUrlFilter productUrlFilter,
    ILogger<ProductUrlDiscoveryService> logger) : IProductUrlDiscoveryService
{
    public async Task<ProductUrlDiscoveryResult> DiscoverProductUrlsAsync(CancellationToken ct)
    {
        try
        {
            var sitemapUrls = productUrlFilter.Apply(
                await sitemapSource.DiscoverProductUrlsAsync(ct),
                "sitemap");
            if (sitemapUrls.Count > 0)
            {
                return new ProductUrlDiscoveryResult(
                    ProductUrlDiscoverySourceKind.Sitemap,
                    sitemapUrls);
            }

            logger.LogWarning(
                "Sitemap product URL discovery returned no URLs. Trying category seed fallback. Reason=EmptySitemapResult");
        }
        catch (SitemapUnavailableException ex)
        {
            logger.LogWarning(
                ex,
                "Sitemap product URL discovery unavailable. Trying category seed fallback. Reason={Reason}",
                ex.Message);
        }

        var categoryUrls = productUrlFilter.Apply(
            await categorySource.DiscoverProductUrlsAsync(ct),
            "category-seed");
        if (categoryUrls.Count > 0)
        {
            logger.LogInformation(
                "Product URL discovery completed using category seed fallback. ProductUrlCount={ProductUrlCount}",
                categoryUrls.Count);
            return new ProductUrlDiscoveryResult(
                ProductUrlDiscoverySourceKind.CategorySeed,
                categoryUrls);
        }

        const string message =
            "Product URL discovery failed. No product URLs available from sitemap or category seed fallback.";
        logger.LogError(
            "{Message} FailureKind={FailureKind}",
            message,
            CrawlerErrorCodes.ProductUrlDiscoveryUnavailable);
        throw new ProductUrlDiscoveryUnavailableException(message);
    }
}
