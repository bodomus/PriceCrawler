using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging;

using PriceCrawler.Application.Abstractions;
using PriceCrawler.Application.Models;
using PriceCrawler.Domain.Enums;
using PriceCrawler.Domain.Interfaces;
using PriceCrawler.Domain.Models;

namespace PriceCrawler.Application.UseCases;

public sealed record PriceCollectionQueueCallbacks(
    Func<ReservedQueueItem, ProductCard, ProductObservationWriteResult, ProductExtractResult, CancellationToken, Task>?
        OnItemSucceeded = null,
    Func<ReservedQueueItem, ProductExtractIssue, CancellationToken, Task>? OnItemDead = null);

public sealed class PriceCollectionQueueProcessor(
    IPriceCollectQueueRepository queueRepository,
    IPriceSnapshotRepository priceSnapshotRepository,
    IProductCardExtractor extractor,
    IListingPageExtractor listingExtractor,
    ICrawlerProgressReporter progressReporter,
    ILogger<PriceCollectionQueueProcessor> logger)
{
    public async Task DrainQueueAsync(
        long runId,
        CrawlerOptions crawlerOptionsValue,
        QueueOptions queueOptionsValue,
        PriceCollectionQueueCallbacks? callbacks,
        CancellationToken ct)
    {
        var batchSize = Math.Max(1, queueOptionsValue.BatchSize);
        var pollDelay = TimeSpan.FromMilliseconds(Math.Max(10, queueOptionsValue.PollDelayMs));
        var leaseDuration = TimeSpan.FromSeconds(Math.Max(1, queueOptionsValue.LeaseSeconds));
        var reaperInterval = TimeSpan.FromSeconds(Math.Max(1, queueOptionsValue.ReaperIntervalSeconds));
        var nextReaperAt = DateTimeOffset.UtcNow;
        var workerId = BuildWorkerId();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (DateTimeOffset.UtcNow >= nextReaperAt)
            {
                var reaped = await queueRepository.ReapExpiredReservationsAsync(runId, ct);
                if (reaped > 0)
                {
                    logger.LogWarning("Recovered stuck queue items run_id={RunId} recovered={Recovered}", runId,
                        reaped);
                }

                nextReaperAt = DateTimeOffset.UtcNow.Add(reaperInterval);
            }

            var batch = await queueRepository.ReserveBatchAsync(runId, batchSize, workerId, leaseDuration, ct);
            if (batch.Count == 0)
            {
                var hasOutstanding = await queueRepository.HasOutstandingItemsAsync(runId, ct);
                if (!hasOutstanding)
                {
                    return;
                }

                await Task.Delay(pollDelay, ct);
                continue;
            }

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, crawlerOptionsValue.MaxConcurrency),
                CancellationToken = ct
            };

            await Parallel.ForEachAsync(batch, parallelOptions,
                async (item, itemCt) =>
                {
                    await ProcessQueueItemAsync(runId, item, queueOptionsValue, callbacks, itemCt);
                });
        }
    }

    private async Task ProcessQueueItemAsync(
        long runId,
        ReservedQueueItem item,
        QueueOptions queueOpt,
        PriceCollectionQueueCallbacks? callbacks,
        CancellationToken ct)
    {
        try
        {
            progressReporter.SetCurrentItem(item.Url);
            var pageKind = ResolvePageKind(item);
            if (pageKind == QueueItemKind.ListingPage || pageKind == QueueItemKind.CategoryPage)
            {
                await ProcessListingQueueItemAsync(runId, item, pageKind, queueOpt, callbacks, ct);
                return;
            }

            var extractResult = await extractor.ExtractAsync(item.Url, ct);
            if (!extractResult.HasCard || extractResult.Card is null)
            {
                var issue = NormalizeIssue(extractResult.Issue, isCritical: true);
                var finalFailure = await FinalizeFailedItemAsync(
                    runId,
                    item,
                    issue,
                    queueOpt,
                    callbacks,
                    ct);

                logger.LogWarning(
                    "Queue item failed run_id={RunId} queue_id={QueueId} url={Url} error_code={ErrorCode} http_status={HttpStatus} transient={Transient}",
                    runId,
                    item.Id,
                    item.Url,
                    issue.ErrorCode,
                    issue.HttpStatus,
                    issue.IsTransient);
                if (finalFailure)
                {
                    progressReporter.IncrementProductProcessed();
                    progressReporter.IncrementProductFailed();
                }

                return;
            }

            var card = extractResult.Card;
            var observation = new ProductObservation(
                card.ExternalId,
                card.Name,
                card.Url,
                card.Slug,
                card.PackValue,
                card.PackUnit,
                card.Price,
                card.OldPrice,
                card.PromoFlag,
                card.InStock,
                DateTimeOffset.UtcNow);

            var writeResult = await priceSnapshotRepository.StoreObservationAsync(
                runId,
                item.Id,
                observation,
                ct);

            if (extractResult.Issue is not null)
            {
                var issue = NormalizeIssue(extractResult.Issue, isCritical: false);
                await priceSnapshotRepository.InsertCrawlErrorAsync(
                    new CrawlErrorRecord(
                        runId,
                        item.Id,
                        writeResult.ProductId,
                        card.Url,
                        DateTimeOffset.UtcNow,
                        issue.ErrorCode,
                        issue.HttpStatus,
                        issue.Message),
                    ct);
            }

            if (callbacks?.OnItemSucceeded is not null)
            {
                await callbacks.OnItemSucceeded(item, card, writeResult, extractResult, ct);
            }

            await queueRepository.MarkSucceededAsync(item.Id, ct);
            progressReporter.IncrementProductProcessed();
            progressReporter.IncrementProductSucceeded();

            logger.LogDebug(
                "Queue item succeeded run_id={RunId} queue_id={QueueId} external_id={ExternalId} latency_ms={LatencyMs} http_status={HttpStatus}",
                runId,
                item.Id,
                card.ExternalId,
                extractResult.LatencyMs,
                extractResult.Issue?.HttpStatus);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var issue = new ProductExtractIssue(
                "process",
                CrawlerErrorCodes.Unknown,
                null,
                TrimMessage(ex.Message),
                null,
                true,
                true);
            try
            {
                var finalFailure = await FinalizeFailedItemAsync(
                    runId,
                    item,
                    issue,
                    queueOpt,
                    callbacks,
                    ct);
                if (finalFailure)
                {
                    progressReporter.IncrementProductProcessed();
                    progressReporter.IncrementProductFailed();
                }
            }
            catch (Exception persistEx)
            {
                logger.LogWarning(
                    persistEx,
                    "Queue item failure persistence failed run_id={RunId} queue_id={QueueId}",
                    runId,
                    item.Id);
            }

            logger.LogWarning(ex, "Queue item processing failed run_id={RunId} queue_id={QueueId}", runId,
                item.Id);
        }
    }

    private async Task<bool> FinalizeFailedItemAsync(
        long runId,
        ReservedQueueItem item,
        ProductExtractIssue issue,
        QueueOptions queueOpt,
        PriceCollectionQueueCallbacks? callbacks,
        CancellationToken ct)
    {
        var failureAttempt = item.Attempt + 1;
        var action = QueueRetryPolicy.DecideFailureAction(issue.IsTransient, failureAttempt, item.MaxAttempts);

        if (action == QueueFailureAction.Retry)
        {
            var jitterMax = Math.Max(1, queueOpt.RetryBaseDelayMs);
            var jitterMs = Random.Shared.Next(0, jitterMax);
            var delay = QueueRetryPolicy.ComputeBackoffDelay(
                failureAttempt,
                queueOpt.RetryBaseDelayMs,
                queueOpt.RetryMaxDelayMs,
                jitterMs);
            if (string.Equals(issue.ErrorCode, CrawlerErrorCodes.TooManyRequests, StringComparison.OrdinalIgnoreCase))
            {
                var doubledMs = Math.Min(delay.TotalMilliseconds * 2d, Math.Max(queueOpt.RetryMaxDelayMs, 1));
                delay = TimeSpan.FromMilliseconds(doubledMs);
            }

            await queueRepository.MarkRetryAsync(item.Id, issue.ErrorCode, issue.HttpStatus, issue.Message,
                DateTimeOffset.UtcNow.Add(delay), ct);
            return false;
        }

        await priceSnapshotRepository.InsertCrawlErrorAsync(
            new CrawlErrorRecord(
                runId,
                item.Id,
                null,
                item.Url,
                DateTimeOffset.UtcNow,
                issue.ErrorCode,
                issue.HttpStatus,
                issue.Message),
            ct);

        await queueRepository.MarkDeadAsync(item.Id, issue.ErrorCode, issue.HttpStatus, issue.Message, ct);
        if (callbacks?.OnItemDead is not null)
        {
            await callbacks.OnItemDead(item, issue, ct);
        }

        return true;
    }

    private async Task ProcessListingQueueItemAsync(
        long runId,
        ReservedQueueItem item,
        QueueItemKind pageKind,
        QueueOptions queueOpt,
        PriceCollectionQueueCallbacks? callbacks,
        CancellationToken ct)
    {
        var result = await listingExtractor.ExtractAsync(item.Url, ct);
        var issue = NormalizeIssue(result.Issue, isCritical: result.FoundCount == 0);

        if (result.Issue is not null &&
            !string.Equals(result.Issue.ErrorCode, CrawlerErrorCodes.ListingNoProductsFound,
                StringComparison.OrdinalIgnoreCase))
        {
            var finalFailure = await FinalizeFailedItemAsync(runId, item, issue, queueOpt, callbacks, ct);
            logger.LogWarning(
                "Listing queue item failed run_id={RunId} queue_id={QueueId} url={Url} page_kind={PageKind} extractor={Extractor} http_status={HttpStatus} error_code={ErrorCode} transient={Transient}",
                runId,
                item.Id,
                item.Url,
                pageKind,
                nameof(IListingPageExtractor),
                issue.HttpStatus,
                issue.ErrorCode,
                issue.IsTransient);

            if (finalFailure)
            {
                progressReporter.IncrementListingProcessed();
                progressReporter.IncrementListingFailed();
            }

            return;
        }

        if (result.Issue is not null)
        {
            await priceSnapshotRepository.InsertCrawlErrorAsync(
                new CrawlErrorRecord(
                    runId,
                    item.Id,
                    null,
                    item.Url,
                    DateTimeOffset.UtcNow,
                    issue.ErrorCode,
                    issue.HttpStatus,
                    issue.Message),
                ct);
        }

        var discoveredItems = result.ProductUrls
            .Select(url => new QueueEnqueueItem(
                url,
                BuildDiscoveredProductIdempotencyKey(runId, item.Id, url),
                null,
                QueueItemKind.ProductPage))
            .ToList();

        var enqueued = discoveredItems.Count == 0
            ? 0
            : await queueRepository.EnqueueAsync(runId, discoveredItems, Math.Max(1, queueOpt.MaxAttempts), ct);
        progressReporter.IncrementProductLinksDiscoveredFromListings(result.FoundCount);
        progressReporter.IncrementProductLinksEnqueuedFromListings(enqueued);
        progressReporter.IncrementProductQueueTotal(enqueued);

        await queueRepository.MarkSucceededAsync(item.Id, ct);
        progressReporter.IncrementListingProcessed();
        progressReporter.IncrementListingSucceeded();

        logger.LogInformation(
            "Listing page parsed run_id={RunId} queue_id={QueueId} url={Url} page_kind={PageKind} extractor={Extractor} http_status={HttpStatus} found_product_links={FoundProductLinks} enqueued_product_links={EnqueuedProductLinks} error_code={ErrorCode} transient={Transient}",
            runId,
            item.Id,
            item.Url,
            pageKind,
            nameof(IListingPageExtractor),
            result.HttpStatus,
            result.FoundCount,
            enqueued,
            result.Issue?.ErrorCode ?? CrawlerErrorCodes.ListingParsed,
            result.IsTransient);
    }

    public static string BuildWorkerId()
        => $"{Environment.MachineName}:{Environment.ProcessId}";

    public static string NormalizeErrorCode(string? errorCode) =>
        errorCode switch
        {
            CrawlerErrorCodes.NotFound => CrawlerErrorCodes.NotFound,
            CrawlerErrorCodes.TooManyRequests => CrawlerErrorCodes.TooManyRequests,
            CrawlerErrorCodes.Timeout => CrawlerErrorCodes.Timeout,
            CrawlerErrorCodes.Http5xx => CrawlerErrorCodes.Http5xx,
            CrawlerErrorCodes.ParseFailed => CrawlerErrorCodes.ParseFailed,
            CrawlerErrorCodes.ListingParsed => CrawlerErrorCodes.ListingParsed,
            CrawlerErrorCodes.ListingNoProductsFound => CrawlerErrorCodes.ListingNoProductsFound,
            CrawlerErrorCodes.ProductLinksDiscovered => CrawlerErrorCodes.ProductLinksDiscovered,
            CrawlerErrorCodes.ListingPageSentToProductExtractor => CrawlerErrorCodes.ListingPageSentToProductExtractor,
            CrawlerErrorCodes.UnsupportedPageType => CrawlerErrorCodes.UnsupportedPageType,
            _ => CrawlerErrorCodes.Unknown
        };

    public static string TrimMessage(string? message)
    {
        const int maxLength = 400;
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var trimmed = message.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    public static ProductExtractIssue NormalizeIssue(ProductExtractIssue? issue, bool isCritical)
    {
        if (issue is null)
        {
            return new ProductExtractIssue(
                "extract",
                CrawlerErrorCodes.Unknown,
                null,
                string.Empty,
                null,
                false,
                isCritical);
        }

        return issue with
        {
            Stage = string.IsNullOrWhiteSpace(issue.Stage) ? "extract" : issue.Stage.Trim().ToLowerInvariant(),
            ErrorCode = NormalizeErrorCode(issue.ErrorCode),
            Message = TrimMessage(issue.Message),
            IsCritical = isCritical
        };
    }

    private static QueueItemKind ResolvePageKind(ReservedQueueItem item)
    {
        var urlKind = VarusPageKindClassifier.Classify(item.Url);
        if (urlKind == QueueItemKind.ListingPage)
        {
            return QueueItemKind.ListingPage;
        }

        return item.PageKind == QueueItemKind.Unknown ? urlKind : item.PageKind;
    }

    private static string BuildDiscoveredProductIdempotencyKey(long runId, long listingQueueId, string normalizedUrl)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes($"{runId}:listing:{listingQueueId}:{normalizedUrl.Trim()}");
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
