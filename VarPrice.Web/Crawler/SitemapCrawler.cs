using Microsoft.Extensions.Options;

namespace VarPrice.Web.Crawler;

public interface ISitemapCrawler
{
    Task<SitemapCrawlResult> CollectPageUrlsAsync(Uri start, CancellationToken ct);
}

public sealed record SitemapCrawlResult(int SitemapsDiscovered, IReadOnlyList<Uri> PageUrls);

public sealed class SitemapCrawler(
    IVarusHttpClient http,
    ISitemapParser parser,
    IOptions<CrawlerOptions> options,
    ILogger<SitemapCrawler> log) : ISitemapCrawler
{
    public async Task<SitemapCrawlResult> CollectPageUrlsAsync(Uri start, CancellationToken ct)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pages = new List<Uri>();
        var queue = new Queue<Uri>();
        queue.Enqueue(start);

        while (queue.Count > 0 &&
               pages.Count < options.Value.MaxUrlsToCollect &&
               visited.Count < options.Value.MaxSitemapsToVisit)
        {
            ct.ThrowIfCancellationRequested();

            var sitemapUrl = queue.Dequeue();
            if (!visited.Add(sitemapUrl.AbsoluteUri))
            {
                continue;
            }

            var xml = await http.GetStringAsync(sitemapUrl, ct);
            var kind = parser.Detect(xml);

            if (kind == SitemapDocKind.SitemapIndex)
            {
                foreach (var child in parser.ParseSitemapIndexLocs(xml))
                {
                    queue.Enqueue(child);
                }
            }
            else if (kind == SitemapDocKind.UrlSet)
            {
                pages.AddRange(parser.ParseUrlSetLocs(xml));
            }
        }

        log.LogInformation("Sitemaps visited={Visited}, pages collected={Pages}", visited.Count, pages.Count);
        return new SitemapCrawlResult(visited.Count, pages);
    }
}
