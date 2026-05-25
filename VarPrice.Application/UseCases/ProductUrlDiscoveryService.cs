using Microsoft.Extensions.Logging;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;

namespace VarPrice.Application.UseCases;

public sealed class ProductUrlDiscoveryService(
    IProductUrlDiscoveryStrategyFactory strategyFactory,
    IProductUrlFilter productUrlFilter,
    ILogger<ProductUrlDiscoveryService> logger) : IProductUrlDiscoveryService
{
    public async Task<ProductUrlDiscoveryResult> DiscoverProductUrlsAsync(CancellationToken ct)
    {
        var strategy = strategyFactory.Create();
        var discoveredItems = await strategy.DiscoverAsync(ct);
        var candidateUrls = discoveredItems
            .Select(x => x.Url)
            .Where(x => Uri.TryCreate(x, UriKind.Absolute, out _))
            .Select(x => new Uri(x));
        var urls = productUrlFilter.Apply(candidateUrls, strategy.SourceName);
        if (urls.Count > 0)
        {
            logger.LogInformation(
                "Product URL discovery completed. DiscoveryMode={DiscoveryMode}; SourceName={SourceName}; ProductUrlCount={ProductUrlCount}",
                strategy.SourceKind,
                strategy.SourceName,
                urls.Count);
            return new ProductUrlDiscoveryResult(strategy.SourceKind, urls);
        }

        var message = $"Product URL discovery failed. No product URLs available from {strategy.SourceName}.";
        logger.LogError(
            "{Message} SourceName={SourceName}; FailureKind={FailureKind}",
            message,
            strategy.SourceName,
            CrawlerErrorCodes.ProductUrlDiscoveryUnavailable);
        throw new ProductUrlDiscoveryUnavailableException(message);
    }
}
