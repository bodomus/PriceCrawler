using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;

namespace VarPrice.Application.UseCases;

public sealed class SitemapProductUrlDiscoveryStrategy(
    IOptions<CrawlerOptions> options,
    IProductUrlSource sitemapReader,
    ILogger<SitemapProductUrlDiscoveryStrategy> logger) : IProductUrlDiscoveryStrategy
{
    public ProductUrlDiscoverySourceKind SourceKind => ProductUrlDiscoverySourceKind.Sitemap;

    public string SourceName => "sitemap";

    public async Task<IReadOnlyCollection<ProductDiscoveryItem>> DiscoverAsync(CancellationToken ct)
    {
        var opt = options.Value;
        var urls = await sitemapReader.GetProductUrlsAsync(opt.SitemapIndexUrl, ct);
        var candidates = urls
            .Where(x => Uri.TryCreate(x, UriKind.Absolute, out _))
            .Select(x => new ProductDiscoveryItem(x, SourceName, opt.SitemapIndexUrl))
            .ToList();

        logger.LogInformation(
            "Sitemap product URL candidates discovered. Count={Count}",
            candidates.Count);

        return candidates;
    }
}
