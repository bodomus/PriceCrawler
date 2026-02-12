using Microsoft.Extensions.Options;
using VarPrice.Web.Storage;

namespace VarPrice.Web.Crawler;

public sealed class CrawlerRunner(
    IOptions<CrawlerOptions> opt,
    ISitemapCrawler sitemapCrawler,
    IProductUrlFilter productUrlFilter,
    IProductCardExtractor extractor,
    ICrawlerRepository repo,
    ILogger<CrawlerRunner> log
)
{
    public async Task<CrawlerRunResult> RunVegetablesAsync(CancellationToken ct)
    {
        var result = new CrawlerRunResult();
        var o = opt.Value;

        var runId = repo.StartRun("sitemap-index");
        result.RunId = runId;

        try
        {
            var crawlResult = await sitemapCrawler.CollectPageUrlsAsync(o.SitemapIndexUrl, ct);
            result.SitemapsDiscovered = crawlResult.SitemapsDiscovered;
            result.UrlsDiscovered = crawlResult.PageUrls.Count;

            var productUrls = crawlResult.PageUrls
                .Where(productUrlFilter.IsProductUrl)
                .Where(u => string.IsNullOrWhiteSpace(o.VegetablesUrlContains) ||
                            u.AbsoluteUri.Contains(o.VegetablesUrlContains, StringComparison.OrdinalIgnoreCase))
                .DistinctBy(u => u.AbsoluteUri)
                .Take(Math.Max(1, o.MaxProductsPerRun))
                .ToList();

            result.ProductUrlsDiscovered = productUrls.Count;

            foreach (var productUrl in productUrls)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var card = await extractor.ExtractAsync(productUrl.AbsoluteUri, ct);
                    result.PagesFetched++;

                    if (card is null)
                    {
                        continue;
                    }

                    result.ItemsParsed++;
                    var pk = await repo.UpsertProductAsync(card.ProductId, card.Name, card.Url, card.PackValue, card.PackUnit, ct);
                    await repo.InsertSnapshotAsync(runId, pk, card.City, card.Price, card.OldPrice, card.PromoFlag, card.InStock, ct);

                    result.ItemsSaved++;
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    log.LogWarning(ex, "Failed to ingest {Url}", productUrl);
                }
            }

            result.ProductsProcessed = result.ItemsSaved;
            result.Status = "ok";
            result.Note =
                $"sitemaps={result.SitemapsDiscovered}, urls={result.UrlsDiscovered}, productUrls={result.ProductUrlsDiscovered}, pages={result.PagesFetched}, parsed={result.ItemsParsed}, saved={result.ItemsSaved}, errors={result.Errors}";

            await repo.FinishRunAsync(runId, result.Status, result.Note, ct);
            log.LogInformation("Crawler finished. {Note}", result.Note);
            return result;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            result.Status = "failed";
            result.Errors++;
            result.LastError = ex.Message;
            result.Note = ex.Message;

            log.LogError(ex, "Crawler failed");
            await repo.FinishRunAsync(runId, result.Status, result.Note, ct);
            return result;
        }
    }
}
