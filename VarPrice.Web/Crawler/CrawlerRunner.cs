using Microsoft.Extensions.Options;
using VarPrice.Web.Storage;

namespace VarPrice.Web.Crawler;

public sealed class CrawlerRunner(
    IOptions<CrawlerOptions> opt,
    ISitemapReader sitemap,
    IProductCardExtractor extractor,
    ICrawlerRepository repo,
    ILogger<CrawlerRunner> log
)
{
    public async Task<CrawlerRunResult> RunVegetablesAsync(CancellationToken ct)
    {

        var o = opt.Value;

        var runId = repo.StartRun("sitemap");
        var processed = 0;
        var errors = 0;

        try
        {
            var urls = await sitemap.GetProductUrlsAsync(o.SitemapIndexUrl, ct);

            if (!string.IsNullOrWhiteSpace(o.VegetablesUrlContains))
                urls = urls.Where(u => u.Contains(o.VegetablesUrlContains, StringComparison.OrdinalIgnoreCase)).ToList();

            urls = urls.Take(Math.Max(1, o.MaxProductsPerRun)).ToList();

            foreach (var url in urls)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var card = await extractor.ExtractAsync(url, ct);
                    if (card is null)
                    {
                        errors++;
                        continue;
                    }

                    var pk = await repo.UpsertProductAsync(card.ProductId, card.Name, card.Url, card.PackValue, card.PackUnit, ct);
                    await repo.InsertSnapshotAsync(runId, pk, card.City, card.Price, card.OldPrice, card.PromoFlag, card.InStock, ct);

                    processed++;
                }
                catch (Exception ex)
                {
                    errors++;
                    log.LogWarning(ex, "Failed to ingest {Url}", url);
                }
            }

            var note = $"processed={processed}, errors={errors}";
            await repo.FinishRunAsync(runId, "ok", note, ct);
            return new CrawlerRunResult(runId, "ok", processed, errors, note);
        }
        catch (Exception ex)
        {
            log.LogError($"App cause error: {ex.Message}");
            await repo.FinishRunAsync(runId, "failed", ex.Message, ct);
            return new CrawlerRunResult(runId, "failed", processed, errors + 1, ex.Message);
        }
    }
}
