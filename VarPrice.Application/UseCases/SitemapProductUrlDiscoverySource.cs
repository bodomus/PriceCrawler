using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;

namespace VarPrice.Application.UseCases;

public sealed class SitemapProductUrlDiscoverySource(
    IOptions<CrawlerOptions> options,
    IOptions<UrlFilterOptions> urlFilterOptions,
    IProductUrlSource sitemapReader,
    ILogger<SitemapProductUrlDiscoverySource> logger) : ISitemapProductUrlDiscoverySource
{
    public async Task<IReadOnlyCollection<Uri>> DiscoverProductUrlsAsync(CancellationToken ct)
    {
        var opt = options.Value;
        var urls = await sitemapReader.GetProductUrlsAsync(opt.SitemapIndexUrl, ct);
        var filtered = ProductUrlFiltering.Apply(
            urls,
            opt,
            urlFilterOptions.Value,
            logger,
            sourceName: "sitemap");

        logger.LogInformation(
            "Product URL discovery completed using sitemap. Count={Count}",
            filtered.Count);

        return filtered;
    }
}
