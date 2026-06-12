using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;
using VarPrice.Domain.Enums;
using VarPrice.Domain.Interfaces;
using VarPrice.Domain.Models;
using VarPrice.Domain.ValueObjects;

namespace VarPrice.Application.UseCases;

public sealed class CollectProductPricesUseCase(
    IOptions<CrawlerOptions> options,
    IOptions<QueueOptions> queueOptions,
    IProductCatalogRepository productCatalogRepository,
    ICrawlerRunRepository crawlerRunRepository,
    IIngestionRunRepository ingestionRunRepository,
    IPriceCollectQueueRepository queueRepository,
    PriceCollectionQueueProcessor queueProcessor,
    ILogger<CollectProductPricesUseCase> logger) : ICollectProductPricesUseCase
{
    public async Task<CollectProductPricesResult> ExecuteAsync(CancellationToken ct)
    {
        var opt = options.Value;
        var queueOpt = queueOptions.Value;
        var limit = Math.Max(1, opt.MaxProductsPerRun);
        var leaseDuration = TimeSpan.FromSeconds(Math.Max(30, opt.CatalogLeaseSeconds));
        var workerId = PriceCollectionQueueProcessor.BuildWorkerId();
        var selectedById = new Dictionary<long, ProductCatalogItem>();
        long runId = 0;
        long ingestionRunId = 0;

        try
        {
            runId = await crawlerRunRepository.StartAsync("price-collection", ct);
            logger.LogInformation("Price collection started. RunId={RunId}; Limit={Limit}", runId, limit);

            ingestionRunId = await ingestionRunRepository.StartAsync(runId, ct);
            var nowUtc = DateTimeOffset.UtcNow;
            var selected = await productCatalogRepository.GetDueProductsAsync(
                limit,
                nowUtc,
                leaseDuration,
                workerId,
                ct);
            selectedById = selected.ToDictionary(x => x.Id);

            logger.LogInformation(
                "Catalog products selected. RunId={RunId}; Selected={Selected}; WorkerId={WorkerId}",
                runId,
                selected.Count,
                workerId);

            if (selected.Count == 0)
            {
                const string emptyNote = "no due catalog products";
                await ingestionRunRepository.FinishAsync(ingestionRunId, RunStatus.Ok, null, ct);
                await crawlerRunRepository.FinishAsync(runId, RunStatus.Ok, emptyNote, ct);
                return new CollectProductPricesResult(runId, "ok", 0, 0, 0, 0, 0, 0, null, emptyNote);
            }

            var queueItems = selected
                .Select(item => new QueueEnqueueItem(
                    item.Url,
                    BuildIdempotencyKey(runId, item.Id, item.NormalizedUrl),
                    item.Id))
                .ToList();
            var enqueued = await queueRepository.EnqueueAsync(runId, queueItems, Math.Max(1, queueOpt.MaxAttempts), ct);
            logger.LogInformation(
                "Price collection queue seeded. RunId={RunId}; Selected={Selected}; Enqueued={Enqueued}",
                runId,
                selected.Count,
                enqueued);

            var callbacks = new PriceCollectionQueueCallbacks(
                OnItemSucceeded: async (item, card, _, _, itemCt) =>
                {
                    if (item.ProductCatalogId is null)
                    {
                        return;
                    }

                    var checkedAt = DateTimeOffset.UtcNow;
                    await productCatalogRepository.MarkCheckedAsync(
                        new ProductCatalogCheckSuccess(
                            item.ProductCatalogId.Value,
                            checkedAt,
                            checkedAt.AddHours(Math.Max(1, opt.SuccessfulCheckIntervalHours)),
                            card.ExternalId,
                            card.Slug),
                        itemCt);
                },
                OnItemDead: async (item, _, itemCt) =>
                {
                    if (item.ProductCatalogId is null ||
                        !selectedById.TryGetValue(item.ProductCatalogId.Value, out var catalogItem))
                    {
                        return;
                    }

                    var attemptedAt = DateTimeOffset.UtcNow;
                    var delay = ProductCatalogRetryPolicy.ComputeDelay(
                        catalogItem.ConsecutiveErrors,
                        opt.CatalogFailureBaseDelayMinutes,
                        opt.CatalogFailureMaxDelayHours);
                    await productCatalogRepository.MarkFailedAsync(
                        new ProductCatalogCheckFailure(
                            item.ProductCatalogId.Value,
                            attemptedAt,
                            attemptedAt.Add(delay)),
                        itemCt);
                });

            await queueProcessor.DrainQueueAsync(runId, opt, queueOpt, callbacks, ct);
            var stats = await queueRepository.GetRunStatsAsync(runId, ct);
            var runStatus = stats.Dead > 0 ? RunStatus.Error : RunStatus.Ok;
            var note =
                $"selected={selected.Count}, enqueued={enqueued}, succeeded={stats.Succeeded}, retry={stats.Retry}, dead={stats.Dead}";

            await ingestionRunRepository.FinishAsync(ingestionRunId, runStatus, null, ct);
            await crawlerRunRepository.FinishAsync(runId, runStatus, note, ct);

            logger.LogInformation(
                "Price collection completed. RunId={RunId}; Selected={Selected}; Enqueued={Enqueued}; Succeeded={Succeeded}; Retry={Retry}; Dead={Dead}",
                runId,
                selected.Count,
                enqueued,
                stats.Succeeded,
                stats.Retry,
                stats.Dead);

            return new CollectProductPricesResult(
                runId,
                runStatus == RunStatus.Ok ? "ok" : "error",
                selected.Count,
                enqueued,
                stats.Succeeded,
                stats.Dead,
                stats.Retry,
                stats.Dead,
                stats.Dead > 0 ? "price_collection_dead_items" : null,
                note);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (ingestionRunId > 0)
            {
                await ingestionRunRepository.FinishAsync(
                    ingestionRunId,
                    RunStatus.Error,
                    new ErrorInfo("price_collection_cancelled", "Price collection was cancelled."),
                    CancellationToken.None);
            }

            if (runId > 0)
            {
                await crawlerRunRepository.FinishAsync(
                    runId,
                    RunStatus.Error,
                    "Price collection was cancelled.",
                    CancellationToken.None);
            }

            throw;
        }
        catch (Exception ex)
        {
            const string errorCode = "price_collection_failed";
            logger.LogError(ex, "Price collection failed. RunId={RunId}; ErrorCode={ErrorCode}", runId, errorCode);
            if (ingestionRunId > 0)
            {
                await ingestionRunRepository.FinishAsync(
                    ingestionRunId,
                    RunStatus.Error,
                    new ErrorInfo(errorCode, ex.Message),
                    CancellationToken.None);
            }

            if (runId > 0)
            {
                await crawlerRunRepository.FinishAsync(runId, RunStatus.Error, ex.Message, CancellationToken.None);
            }

            return new CollectProductPricesResult(runId, "error", 0, 0, 0, 1, 0, 0, errorCode, ex.Message);
        }
    }

    private static string BuildIdempotencyKey(long runId, long catalogItemId, string normalizedUrl)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes($"{runId}:{catalogItemId}:{normalizedUrl.Trim()}");
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
