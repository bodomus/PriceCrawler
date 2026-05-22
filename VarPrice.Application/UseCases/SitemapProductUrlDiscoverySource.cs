using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;

namespace VarPrice.Application.UseCases;

public sealed class SitemapProductUrlDiscoverySource(
    IOptions<CrawlerOptions> options,
    IProductUrlSource sitemapReader,
    ILogger<SitemapProductUrlDiscoverySource> logger) : ISitemapProductUrlDiscoverySource
{
    public async Task<IReadOnlyCollection<Uri>> DiscoverProductUrlsAsync(CancellationToken ct)
    {
        var opt = options.Value;
        var urls = await sitemapReader.GetProductUrlsAsync(opt.SitemapIndexUrl, ct);
        var candidates = urls
            .Where(x => Uri.TryCreate(x, UriKind.Absolute, out _))
            .Select(x => new Uri(x))
            .ToList();

        logger.LogInformation(
            "Sitemap product URL candidates discovered. Count={Count}",
            candidates.Count);

        return candidates;
    }
}
