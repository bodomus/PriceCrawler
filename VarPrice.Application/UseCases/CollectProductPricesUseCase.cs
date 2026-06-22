using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;
using VarPrice.Domain.Constants;
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
        var duration = Stopwatch.StartNew();
        var metrics = new CrawlerRunMetrics();
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
            runId = await crawlerRunRepository.StartAsync(
                CrawlerRunTypes.PriceCollection, CrawlerRunTypes.PriceCollection, null, ct);
            logger.LogInformation("Price collection started. RunId={RunId}; Limit={Limit}", runId, limit);

            ingestionRunId = await ingestionRunRepository.StartAsync(runId, ct);
            var nowUtc = DateTimeOffset.UtcNow;
            var selectionWatch = Stopwatch.StartNew();
            var selected =
                await productCatalogRepository.GetDueProductsAsync(limit, nowUtc, leaseDuration, workerId, ct);
            selectionWatch.Stop();
            metrics.AddStage(CrawlerRunStages.CatalogSelection, selectionWatch.ElapsedMilliseconds, selected.Count);
            selectedById = selected.ToDictionary(x => x.Id);
            metrics.SetSelection(selected.Count, 0);

            logger.LogInformation(
                "Catalog products selected. RunId={RunId}; Selected={Selected}; WorkerId={WorkerId}",
                runId,
                selected.Count,
                workerId);

            if (selected.Count == 0)
            {
                const string emptyNote = "no due catalog products";
                await ingestionRunRepository.FinishAsync(ingestionRunId, RunStatus.Ok, null, ct);
                metrics.AddStage(CrawlerRunStages.RunFinalization, 0);
                await crawlerRunRepository.CompleteAsync(runId, RunStatus.Ok, metrics.Snapshot(),
                    metrics.StageTimings(),
                    emptyNote, null, null, ct);
                duration.Stop();
                return BuildResult(runId, RunStatus.Ok, metrics, null, emptyNote, duration.ElapsedMilliseconds);
            }

            var queueItems = selected
                .Select(item => new QueueEnqueueItem(
                    item.Url,
                    BuildIdempotencyKey(runId, item.Id, item.NormalizedUrl),
                    item.Id))
                .ToList();
            int enqueued;
            try
            {
                var enqueueWatch = Stopwatch.StartNew();
                enqueued = await queueRepository.EnqueueAsync(runId, queueItems, Math.Max(1, queueOpt.MaxAttempts), ct);
                enqueueWatch.Stop();
                metrics.AddStage(CrawlerRunStages.QueueEnqueue, enqueueWatch.ElapsedMilliseconds, enqueued);
                metrics.SetSelection(selected.Count, enqueued);
            }
            catch
            {
                await ReleaseCatalogReservationsAsync(selected, ct);
                throw;
            }

            if (enqueued < selected.Count)
            {
                await ReleaseCatalogReservationsAsync(selected.Skip(enqueued).ToList(), ct);
            }

            logger.LogInformation(
                "Price collection queue seeded. RunId={RunId}; Selected={Selected}; Enqueued={Enqueued}",
                runId,
                selected.Count,
                enqueued);

            var callbacks = new PriceCollectionQueueCallbacks(
                OnItemSucceeded: async (item, card, write, extract, itemCt) =>
                {
                    metrics.RecordObservation(write.ProductCreated, write.SnapshotCreated, extract.Issue is not null);
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
                    metrics.IncrementError();
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

            var processingWatch = Stopwatch.StartNew();
            await queueProcessor.DrainQueueAsync(runId, opt, queueOpt, callbacks, ct);
            processingWatch.Stop();
            metrics.AddStage(CrawlerRunStages.QueueProcessing, processingWatch.ElapsedMilliseconds, enqueued);
            var stats = await queueRepository.GetRunStatsAsync(runId, ct);
            metrics.SetQueue(stats.Succeeded, stats.Retry, stats.Dead);
            var runStatus = stats.Dead > 0 ? RunStatus.Error : RunStatus.Ok;
            var failedCount = stats.Retry + stats.Dead;
            var note =
                $"selected={selected.Count}, enqueued={enqueued}, succeeded={stats.Succeeded}, retry={stats.Retry}, dead={stats.Dead}";

            await ingestionRunRepository.FinishAsync(ingestionRunId, runStatus, null, ct);
            metrics.AddStage(CrawlerRunStages.RunFinalization, 0);
            await crawlerRunRepository.CompleteAsync(runId, runStatus, metrics.Snapshot(), metrics.StageTimings(), note,
                stats.Dead > 0 ? "price_collection_dead_items" : null,
                stats.Dead > 0 ? note : null, ct);
            duration.Stop();

            logger.LogInformation(
                "Price collection completed. RunId={RunId}; Selected={Selected}; Enqueued={Enqueued}; Succeeded={Succeeded}; Retry={Retry}; Dead={Dead}",
                runId,
                selected.Count,
                enqueued,
                stats.Succeeded,
                stats.Retry,
                stats.Dead);

            return BuildResult(runId, runStatus, metrics,
                stats.Dead > 0 ? "price_collection_dead_items" : null, note, duration.ElapsedMilliseconds);
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
                await CompletePartialAsync(runId, metrics, "price_collection_cancelled",
                    "Price collection was cancelled.");
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
                await CompletePartialAsync(runId, metrics, errorCode, ex.Message);
            }

            duration.Stop();
            return BuildResult(runId, RunStatus.Error, metrics, errorCode, ex.Message, duration.ElapsedMilliseconds);
        }
    }

    private async Task CompletePartialAsync(long runId, CrawlerRunMetrics metrics, string errorCode, string message)
    {
        try
        {
            var queue = await queueRepository.GetRunStatsAsync(runId, CancellationToken.None);
            metrics.SetQueue(queue.Succeeded, queue.Retry, queue.Dead);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read final queue statistics. RunId={RunId}", runId);
        }

        await crawlerRunRepository.CompleteAsync(runId, RunStatus.Error, metrics.Snapshot(), metrics.StageTimings(),
            message, errorCode, message, CancellationToken.None);
    }

    private static CollectProductPricesResult BuildResult(long runId, RunStatus status, CrawlerRunMetrics metrics,
        string? errorCode, string? message, long durationMs)
    {
        var s = metrics.Snapshot();
        return new CollectProductPricesResult(runId, status == RunStatus.Ok ? "ok" : "error", s.SelectedCount,
            s.EnqueuedCount, s.SucceededCount, s.FailedCount, s.RetryCount, s.DeadCount, errorCode, message,
            s.ProductsCreatedCount, s.ProductsUpdatedCount, s.SnapshotsCreatedCount, s.ErrorsCreatedCount,
            durationMs, s, metrics.StageTimings());
    }

    private static string BuildIdempotencyKey(long runId, long catalogItemId, string normalizedUrl)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes($"{runId}:{catalogItemId}:{normalizedUrl.Trim()}");
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task ReleaseCatalogReservationsAsync(
        IReadOnlyCollection<ProductCatalogItem> catalogItems,
        CancellationToken ct)
    {
        if (catalogItems.Count == 0)
        {
            return;
        }

        var released = await productCatalogRepository.ReleaseReservationsAsync(
            catalogItems.Select(x => x.Id).ToArray(),
            ct);
        logger.LogWarning(
            "Released catalog reservations after queue enqueue mismatch. Released={Released}; Requested={Requested}",
            released,
            catalogItems.Count);
    }
}
