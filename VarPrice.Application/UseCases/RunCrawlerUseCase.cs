using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;
using VarPrice.Domain.Enums;
using VarPrice.Domain.Interfaces;
using VarPrice.Domain.ValueObjects;

namespace VarPrice.Application.UseCases;

public sealed class RunCrawlerUseCase(
    IOptions<CrawlerOptions> options,
    IOptions<UrlFilterOptions> urlFilterOptions,
    IProductUrlSource sitemapReader,
    IProductCardExtractor extractor,
    ICrawlerRunRepository crawlerRunRepository,
    IIngestionRunRepository ingestionRunRepository,
    IPriceSnapshotRepository priceSnapshotRepository,
    ILogger<RunCrawlerUseCase> logger)
{
    public async Task<CrawlerRunResult> RunVegetablesAsync(CancellationToken ct)
    {
        var opt = options.Value;
        var runId = await crawlerRunRepository.StartAsync("sitemap", ct);
        var ingestionRunId = await ingestionRunRepository.StartAsync(runId, ct);

        var processed = 0;
        var errors = 0;

        try
        {
            //There is place where filtering urls

            var urls = await sitemapReader.GetProductUrlsAsync(opt.SitemapIndexUrl, ct);
            if (!string.IsNullOrWhiteSpace(opt.VegetablesUrlContains))
            {
                urls = urls.Where(x => x.Contains(opt.VegetablesUrlContains, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            var excluded = urlFilterOptions.Value.ExcludedUrlSubstrings;
            if (excluded.Length > 0)
            {
                urls = urls
                    .Where(u => !excluded.Any(ex => u.Contains(ex, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }
            //TODO remove in prodaction
            urls = urls.Take(Math.Max(1, opt.MaxProductsPerRun)).ToList();
            int urls_count = urls.Count;
            foreach (var url in urls)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    logger.LogInformation($"Current: {errors+processed} from {urls_count} url: {url}");
                    var card = await extractor.ExtractAsync(url, ct);
                    if (card is null)
                    {
                        //TODO add error code and log url
                        errors++;
                        await priceSnapshotRepository.InsertProductErrorAsync(runId, null, "Unknown", 0, 0, false, false, ct);
                        continue;
                    }

                    var productKey = await priceSnapshotRepository.UpsertProductAsync(card.ProductId, card.Name, card.Url, card.PackValue, card.PackUnit, ct);
                    await priceSnapshotRepository.InsertSnapshotAsync(runId, productKey, card.City, card.Price, card.OldPrice, card.PromoFlag, card.InStock, ct);
                    processed++;
                    logger.LogInformation($"Add url: {url} Name: {card.Name} ProductId:{card.ProductId} PackValue: {card.PackValue} PackUnit");
                }
                catch (Exception ex)
                {
                    errors++;
                    logger.LogWarning(ex, "Failed to ingest {Url}", url);
                }
            }

            var note = $"processed={processed}, errors={errors}";
            await ingestionRunRepository.FinishAsync(ingestionRunId, RunStatus.Ok, null, ct);
            await crawlerRunRepository.FinishAsync(runId, RunStatus.Ok, note, ct);

            return new CrawlerRunResult(runId, RunStatus.Ok.ToString().ToLowerInvariant(), processed, errors, note);
        }
        catch (Exception ex)
        {
            var errorInfo = new ErrorInfo("crawler_failed", ex.Message);
            await ingestionRunRepository.FinishAsync(ingestionRunId, RunStatus.Error, errorInfo, ct);
            await crawlerRunRepository.FinishAsync(runId, RunStatus.Error, ex.Message, ct);
            return new CrawlerRunResult(runId, RunStatus.Error.ToString().ToLowerInvariant(), processed, errors + 1, ex.Message);
        }
    }
}
