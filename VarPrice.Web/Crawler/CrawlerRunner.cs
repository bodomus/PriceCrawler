using Microsoft.Extensions.Options;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;
using VarPrice.Web.Storage;
using VarPrice.Web.Storage.Db;

namespace VarPrice.Web.Crawler;

public interface ICrawlerRunner
{
    Task<DbResult<CrawlerRunResult>> RunVegetablesAsync(CancellationToken ct);
}

public sealed class CrawlerRunner(
    IOptions<CrawlerOptions> opt,
    IProductUrlSource sitemap,
    IProductCardExtractor extractor,
    ICrawlerRepository repo,
    ILogger<CrawlerRunner> log
) : ICrawlerRunner
{
    public async Task<DbResult<CrawlerRunResult>> RunVegetablesAsync(CancellationToken ct)
    {
        var o = opt.Value;

        var startResult = await repo.StartRunAsync("sitemap", ct);
        if (startResult.IsFailure)
        {
            return DbResult<CrawlerRunResult>.Fail(startResult.Error!);
        }

        var runId = startResult.Value;
        var processed = 0;
        var errors = 0;

        try
        {
            var urls = await sitemap.GetProductUrlsAsync(o.SitemapIndexUrl, ct);

            if (!string.IsNullOrWhiteSpace(o.VegetablesUrlContains))
            {
                urls = urls
                    .Where(u => u.Contains(o.VegetablesUrlContains, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // var excluded = urlFilterOptions.Value.ExcludedUrlSubstrings;
            // if (excluded.Length > 0)
            // {
            //     urls = urls
            //         .Where(u => !excluded.Any(ex => u.Contains(ex, StringComparison.OrdinalIgnoreCase)))
            //         .ToList();
            // }

            urls = urls.Take(Math.Max(1, o.MaxProductsPerRun)).ToList();

            foreach (var url in urls)
            {
                ct.ThrowIfCancellationRequested();

                ProductCard? card;
                try
                {
                    card = await extractor.ExtractAsync(url, ct);
                    if (card is null)
                    {
                        errors++;
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    log.LogWarning(ex, "Failed to extract product card from {Url}", url);
                    continue;
                }

                var upsertResult = await repo.UpsertProductAsync(
                    card.ProductId,
                    card.Name,
                    card.Url,
                    card.PackValue,
                    card.PackUnit,
                    ct);
                if (upsertResult.IsFailure)
                {
                    await TryFinishFailedRunAsync(runId, upsertResult.Error!.UserMessage, ct);
                    return DbResult<CrawlerRunResult>.Fail(upsertResult.Error!);
                }

                var snapshotResult = await repo.InsertSnapshotAsync(
                    runId,
                    upsertResult.Value,
                    card.City,
                    card.Price,
                    card.OldPrice,
                    card.PromoFlag,
                    card.InStock,
                    ct);
                if (snapshotResult.IsFailure)
                {
                    await TryFinishFailedRunAsync(runId, snapshotResult.Error!.UserMessage, ct);
                    return DbResult<CrawlerRunResult>.Fail(snapshotResult.Error!);
                }

                processed++;
            }

            var note = $"processed={processed}, errors={errors}";
            var finishResult = await repo.FinishRunAsync(runId, "ok", note, ct);
            if (finishResult.IsFailure)
            {
                return DbResult<CrawlerRunResult>.Fail(finishResult.Error!);
            }

            return DbResult<CrawlerRunResult>.Success(new CrawlerRunResult(runId, "ok", processed, errors, note));
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Unhandled crawler error for run {RunId}", runId);
            await TryFinishFailedRunAsync(runId, ex.Message, ct);
            return DbResult<CrawlerRunResult>.Success(new CrawlerRunResult(runId, "failed", processed, errors + 1, ex.Message));
        }
    }

    private async Task TryFinishFailedRunAsync(long runId, string note, CancellationToken ct)
    {
        var result = await repo.FinishRunAsync(runId, "failed", note, ct);
        if (result.IsFailure)
        {
            log.LogError(
                "Failed to mark crawler run as failed. RunId={RunId} ErrorCode={Code} CorrelationId={CorrelationId}",
                runId,
                result.Error?.Code,
                result.Error?.CorrelationId);
        }
    }
}
