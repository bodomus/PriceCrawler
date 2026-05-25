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

public sealed class RunCrawlerUseCase(
    IOptions<CrawlerOptions> options,
    IOptions<QueueOptions> queueOptions,
    IProductUrlDiscoveryService productUrlDiscoveryService,
    IProductCardExtractor extractor,
    ICrawlerRunRepository crawlerRunRepository,
    IIngestionRunRepository ingestionRunRepository,
    IPriceCollectQueueRepository queueRepository,
    IPriceSnapshotRepository priceSnapshotRepository,
    ILogger<RunCrawlerUseCase> logger) : IRunCrawlerUseCase
{
    public async Task<CrawlerRunResult> RunVegetablesAsync(CancellationToken ct)
    {
        var opt = options.Value;
        var queueOpt = queueOptions.Value;
        ProductUrlDiscoveryResult discovery;

        try
        {
            discovery = await productUrlDiscoveryService.DiscoverProductUrlsAsync(ct);
        }
        catch (ProductUrlDiscoveryUnavailableException ex)
        {
            return await FinishDiscoveryFailureAsync(
                CrawlerErrorCodes.ProductUrlDiscoveryUnavailable,
                ex.Message,
                ct);
        }
        catch (Exception ex)
        {
            return await FinishDiscoveryFailureAsync("crawler_failed", ex.Message, ct);
        }

        var runId = await crawlerRunRepository.StartAsync(ToCrawlerRunSource(discovery.SourceKind), ct);
        var ingestionRunId = await ingestionRunRepository.StartAsync(runId, ct);

        try
        {
            var queueItems = discovery.Urls
                .Select(url => new QueueEnqueueItem(url, BuildIdempotencyKey(runId, url)))
                .ToList();
            var enqueued = await queueRepository.EnqueueAsync(runId, queueItems, Math.Max(1, queueOpt.MaxAttempts), ct);

            logger.LogInformation(
                "Queue seeded run_id={RunId} urls_total={UrlsTotal} enqueued={Enqueued} max_attempts={MaxAttempts}",
                runId,
                queueItems.Count,
                enqueued,
                Math.Max(1, queueOpt.MaxAttempts));

            await DrainQueueAsync(runId, opt, queueOpt, ct);

            var stats = await queueRepository.GetRunStatsAsync(runId, ct);
            var runStatus = stats.Dead > 0 ? RunStatus.Error : RunStatus.Ok;
            var note =
                $"queued={queueItems.Count}, enqueued={enqueued}, succeeded={stats.Succeeded}, dead={stats.Dead}, pending={stats.Pending}, retry={stats.Retry}";
            logger.LogInformation("Crawler finished run_id={RunId} status={Status} {Note}", runId, runStatus, note);

            await ingestionRunRepository.FinishAsync(ingestionRunId, runStatus, null, ct);
            await crawlerRunRepository.FinishAsync(runId, runStatus, note, ct);

            return new CrawlerRunResult(
                runId,
                runStatus.ToString().ToLowerInvariant(),
                stats.Succeeded,
                stats.Dead,
                note);
        }
        catch (Exception ex)
        {
            var errorInfo = new ErrorInfo("crawler_failed", ex.Message);
            await ingestionRunRepository.FinishAsync(ingestionRunId, RunStatus.Error, errorInfo, ct);
            await crawlerRunRepository.FinishAsync(runId, RunStatus.Error, ex.Message, ct);
            return new CrawlerRunResult(
                runId,
                RunStatus.Error.ToString().ToLowerInvariant(),
                0,
                1,
                ex.Message);
        }
    }

    private async Task<CrawlerRunResult> FinishDiscoveryFailureAsync(string errorCode, string message,
        CancellationToken ct)
    {
        var runId = await crawlerRunRepository.StartAsync("discovery", ct);
        var ingestionRunId = await ingestionRunRepository.StartAsync(runId, ct);
        var errorInfo = new ErrorInfo(errorCode, message);
        await ingestionRunRepository.FinishAsync(ingestionRunId, RunStatus.Error, errorInfo, ct);
        await crawlerRunRepository.FinishAsync(runId, RunStatus.Error, message, ct);
        return new CrawlerRunResult(
            runId,
            RunStatus.Error.ToString().ToLowerInvariant(),
            0,
            1,
            message);
    }

    private static string ToCrawlerRunSource(ProductUrlDiscoverySourceKind sourceKind) =>
        sourceKind switch
        {
            ProductUrlDiscoverySourceKind.CategorySeed => "category-seed",
            ProductUrlDiscoverySourceKind.Api => "api",
            _ => "sitemap"
        };

    private async Task DrainQueueAsync(long runId, CrawlerOptions crawlerOptionsValue, QueueOptions queueOptionsValue,
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
                async (item, itemCt) => { await ProcessQueueItemAsync(runId, item, queueOptionsValue, itemCt); });
        }
    }

    private async Task ProcessQueueItemAsync(long runId, ReservedQueueItem item, QueueOptions queueOpt,
        CancellationToken ct)
    {
        try
        {
            var extractResult = await extractor.ExtractAsync(item.Url, ct);
            if (!extractResult.HasCard || extractResult.Card is null)
            {
                var issue = NormalizeIssue(extractResult.Issue, isCritical: true);
                await FinalizeFailedItemAsync(
                    runId,
                    item,
                    issue,
                    queueOpt,
                    ct);

                logger.LogWarning(
                    "Queue item failed run_id={RunId} queue_id={QueueId} url={Url} error_code={ErrorCode} http_status={HttpStatus} transient={Transient}",
                    runId,
                    item.Id,
                    item.Url,
                    issue.ErrorCode,
                    issue.HttpStatus,
                    issue.IsTransient);
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

            await queueRepository.MarkSucceededAsync(item.Id, ct);

            logger.LogInformation(
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
                await FinalizeFailedItemAsync(
                    runId,
                    item,
                    issue,
                    queueOpt,
                    ct);
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

    private async Task FinalizeFailedItemAsync(
        long runId,
        ReservedQueueItem item,
        ProductExtractIssue issue,
        QueueOptions queueOpt,
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
            return;
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
    }

    private static string BuildWorkerId()
        => $"{Environment.MachineName}:{Environment.ProcessId}";

    private static string BuildIdempotencyKey(long runId, string url)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes($"{runId}:{url.Trim()}");
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeErrorCode(string? errorCode) =>
        errorCode switch
        {
            CrawlerErrorCodes.NotFound => CrawlerErrorCodes.NotFound,
            CrawlerErrorCodes.TooManyRequests => CrawlerErrorCodes.TooManyRequests,
            CrawlerErrorCodes.Timeout => CrawlerErrorCodes.Timeout,
            CrawlerErrorCodes.Http5xx => CrawlerErrorCodes.Http5xx,
            CrawlerErrorCodes.ParseFailed => CrawlerErrorCodes.ParseFailed,
            _ => CrawlerErrorCodes.Unknown
        };

    private static string TrimMessage(string? message)
    {
        const int maxLength = 400;
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var trimmed = message.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static ProductExtractIssue NormalizeIssue(ProductExtractIssue? issue, bool isCritical)
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
}
