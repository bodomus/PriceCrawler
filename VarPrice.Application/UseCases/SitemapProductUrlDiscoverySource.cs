using Microsoft.Extensions.Logging;

using VarPrice.Application.Abstractions;

namespace VarPrice.Application.UseCases;

public sealed class SitemapProductUrlDiscoverySource(
    SitemapProductUrlDiscoveryStrategy strategy,
    ILogger<SitemapProductUrlDiscoverySource> logger) : ISitemapProductUrlDiscoverySource
{
    public async Task<IReadOnlyCollection<Uri>> DiscoverProductUrlsAsync(CancellationToken ct)
    {
        var items = await strategy.DiscoverAsync(ct);
        var candidates = items
            .Select(x => x.Url)
            .Where(x => Uri.TryCreate(x, UriKind.Absolute, out _))
            .Select(x => new Uri(x))
            .ToList();

        logger.LogInformation(
            "Sitemap product URL source completed. Count={Count}",
            candidates.Count);

        return candidates;
    }
}
