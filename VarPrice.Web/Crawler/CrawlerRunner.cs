using AngleSharp;
using Microsoft.Extensions.Options;
using VarPrice.Web.Storage;

namespace VarPrice.Web.Crawler;

public sealed class CrawlerRunner(
    IOptions<CrawlerOptions> opt,
    ISitemapCrawler sitemapCrawler,
    IProductUrlFilter productUrlFilter,
    IVarusHttpClient http,
    IPageKindDetector pageKindDetector,
    IProductCardExtractor extractor,
    ICrawlerRepository repo,
    IIngestionRunRepository ingestionRuns,
    ILogger<CrawlerRunner> log
)
{
    public async Task<CrawlerRunResult> RunVegetablesAsync(CancellationToken ct)
    {
        const string runSource = "sitemap-index";
        var result = new CrawlerRunResult();
        var o = opt.Value;
        var cardsToIngest = new List<ProductCard>();

        var crawlerRunId = repo.StartRun(runSource);
        result.RunId = crawlerRunId;

        try
        {
            var crawlResult = await sitemapCrawler.CollectPageUrlsAsync(o.SitemapIndexUrl, ct);
            result.SitemapsDiscovered = crawlResult.SitemapsDiscovered;
            result.UrlsDiscovered = crawlResult.PageUrls.Count;

            var urls = crawlResult.PageUrls.ToList();
            log.LogInformation("Total urls: {Count}", urls.Count);
            foreach (var u in urls.Take(20))
                log.LogInformation("URL sample: {Url} path={Path}", u.AbsoluteUri, u.AbsolutePath);
            var afterProductFilter = urls.Where(productUrlFilter.IsProductUrl).ToList();
            log.LogInformation("After IsProductUrl: {Count}", afterProductFilter.Count);
            var afterContains = afterProductFilter
                .Where(u => string.IsNullOrWhiteSpace(o.VegetablesUrlContains) ||
                            u.AbsoluteUri.Contains(o.VegetablesUrlContains, StringComparison.OrdinalIgnoreCase)).ToList();
            log.LogInformation("After VegetablesUrlContains: {Count}, contains='{Contains}'",
                afterContains.Count, o.VegetablesUrlContains);
            // var productUrls = crawlResult.PageUrls
            //     .Where(productUrlFilter.IsProductUrl)
            //     .Where(u => string.IsNullOrWhiteSpace(o.VegetablesUrlContains) ||
            //                 u.AbsoluteUri.Contains(o.VegetablesUrlContains, StringComparison.OrdinalIgnoreCase))
            //     .DistinctBy(u => u.AbsoluteUri)
            //     .Take(Math.Max(1, o.MaxProductsPerRun))
            //     .ToList();

            result.ProductUrlsDiscovered = afterContains.Count;

            foreach (var productUrl in afterContains)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var html = await http.GetStringAsync(productUrl, ct);
                    var ctx = BrowsingContext.New(Configuration.Default);
                    var doc = await ctx.OpenAsync(req => req.Content(html), ct);
                    result.PagesFetched++;

                    var kind = pageKindDetector.Detect(doc);
                    if (kind == UrlKind.CategoryPage)
                    {
                        log.LogInformation("Skipping category page: {Url}", productUrl.AbsoluteUri);
                        continue;
                    }

                    if (kind == UrlKind.Unknown)
                    {
                        log.LogWarning("Unknown page type, skipping price parsing: {Url}", productUrl.AbsoluteUri);
                        continue;
                    }

                    var card = await extractor.ExtractAsync(productUrl.AbsoluteUri, doc, ct);

                    if (card is null)
                    {
                        continue;
                    }

                    result.ItemsParsed++;
                    cardsToIngest.Add(card);
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    log.LogWarning(ex, "Failed to crawl {Url}", productUrl);
                }
            }

            result.ProductsProcessed = cardsToIngest.Count;
            await repo.FinishRunAsync(crawlerRunId, "ok", BuildNote(result), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            result.Status = "failed";
            result.Errors++;
            result.LastError = ex.Message;
            result.Note = ex.Message;

            log.LogError(ex, "Crawler failed");
            await repo.FinishRunAsync(crawlerRunId, result.Status, result.Note, ct);
            return result;
        }

        long ingestionRunId;
        try
        {
            ingestionRunId = ingestionRuns.StartIngestion(crawlerRunId, runSource);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            result.Status = "failed";
            result.Errors++;
            result.LastError = ex.Message;
            result.Note = ex.Message;

            log.LogError(ex, "Failed to start ingestion run for crawler run {CrawlerRunId}", crawlerRunId);
            return result;
        }

        try
        {
            foreach (var card in cardsToIngest)
            {
                ct.ThrowIfCancellationRequested();

                var pk = await repo.UpsertProductAsync(card.ProductId, card.Name, card.Url, card.PackValue, card.PackUnit, ct);
                await repo.InsertSnapshotAsync(ingestionRunId, pk, card.City, card.Price, card.OldPrice, card.PromoFlag, card.InStock, ct);

                result.ItemsSaved++;
            }

            result.ProductsProcessed = result.ItemsSaved;
            result.Status = "ok";
            result.Note = BuildNote(result);

            await ingestionRuns.FinishIngestionAsync(ingestionRunId, result.Status, result.Note, ct);
            log.LogInformation("Crawler finished. {Note}", result.Note);
            return result;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            result.Status = "failed";
            result.Errors++;
            result.LastError = ex.Message;
            result.Note = ex.Message;

            log.LogError(ex, "Ingestion failed");
            await ingestionRuns.FailIngestionAsync(ingestionRunId, ex, "CrawlerRunner.RunVegetablesAsync", ct);
            return result;
        }
    }

    private static string BuildNote(CrawlerRunResult result) =>
        $"sitemaps={result.SitemapsDiscovered}, urls={result.UrlsDiscovered}, productUrls={result.ProductUrlsDiscovered}, pages={result.PagesFetched}, parsed={result.ItemsParsed}, saved={result.ItemsSaved}, errors={result.Errors}";
}
