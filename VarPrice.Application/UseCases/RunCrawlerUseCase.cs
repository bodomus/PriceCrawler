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
    IOptions<UrlFilterOptions> urlFilterOptions,
    IProductUrlSource sitemapReader,
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
        var runId = await crawlerRunRepository.StartAsync("sitemap", ct);
        var ingestionRunId = await ingestionRunRepository.StartAsync(runId, ct);

        try
        {
            var urls = await sitemapReader.GetProductUrlsAsync(opt.SitemapIndexUrl, ct);
            if (!string.IsNullOrWhiteSpace(opt.VegetablesUrlContains))
            {
                urls = urls.Where(x => x.Contains(opt.VegetablesUrlContains, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var excluded = urlFilterOptions.Value.ExcludedUrlSubstrings;
            if (excluded.Length > 0)
            {
                urls = urls
                    .Where(u => !excluded.Any(ex => u.Contains(ex, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            var maxProductsPerRun = Math.Max(1, opt.MaxProductsPerRun);
            var maxUrls = Math.Max(1, opt.MaxUrls);
            urls = urls.Take(Math.Min(maxProductsPerRun, maxUrls)).ToList();

            var queueItems = urls
                .Select(url => new QueueEnqueueItem(url, null, BuildIdempotencyKey(runId, url)))
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
            var note =
                $"queued={queueItems.Count}, enqueued={enqueued}, succeeded={stats.Succeeded}, dead={stats.Dead}, pending={stats.Pending}, retry={stats.Retry}";
            logger.LogInformation("Crawler finished run_id={RunId} {Note}", runId, note);

            await ingestionRunRepository.FinishAsync(ingestionRunId, RunStatus.Ok, null, ct);
            await crawlerRunRepository.FinishAsync(runId, RunStatus.Ok, note, ct);

            return new CrawlerRunResult(
                runId,
                RunStatus.Ok.ToString().ToLowerInvariant(),
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
            if (!extractResult.IsSuccess || extractResult.Card is null)
            {
                var errorCode = NormalizeErrorCode(extractResult.ErrorCode);
                var message = TrimMessage(extractResult.Message);
                await FinalizeFailedItemAsync(
                    runId,
                    item,
                    errorCode,
                    extractResult.HttpStatus,
                    message,
                    extractResult.IsTransient,
                    queueOpt,
                    ct);

                logger.LogWarning(
                    "Queue item failed run_id={RunId} queue_id={QueueId} url={Url} error_code={ErrorCode} http_status={HttpStatus} transient={Transient}",
                    runId,
                    item.QueueId,
                    item.Url,
                    errorCode,
                    extractResult.HttpStatus,
                    extractResult.IsTransient);
                return;
            }

            var card = extractResult.Card;
            var productKey = await priceSnapshotRepository.UpsertProductAsync(
                card.ProductId,
                card.Name,
                card.Url,
                card.PackValue,
                card.PackUnit,
                ct);

            await priceSnapshotRepository.InsertSnapshotAsync(
                runId,
                productKey,
                card.City,
                card.Price,
                card.OldPrice,
                card.PromoFlag,
                card.InStock,
                item.QueueId,
                ct);

            await queueRepository.MarkSucceededAsync(item.QueueId, ct);

            logger.LogInformation(
                "Queue item succeeded run_id={RunId} queue_id={QueueId} sku={Sku} latency_ms={LatencyMs} http_status={HttpStatus}",
                runId,
                item.QueueId,
                card.ProductId,
                extractResult.LatencyMs,
                extractResult.HttpStatus);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = TrimMessage(ex.Message);
            try
            {
                await FinalizeFailedItemAsync(
                    runId,
                    item,
                    CrawlerErrorCodes.Unknown,
                    null,
                    message,
                    true,
                    queueOpt,
                    ct);
            }
            catch (Exception persistEx)
            {
                logger.LogWarning(
                    persistEx,
                    "Queue item failure persistence failed run_id={RunId} queue_id={QueueId}",
                    runId,
                    item.QueueId);
            }

            logger.LogWarning(ex, "Queue item processing failed run_id={RunId} queue_id={QueueId}", runId,
                item.QueueId);
        }
    }

    private async Task FinalizeFailedItemAsync(long runId, ReservedQueueItem item, string errorCode, int? httpStatus,
        string? message, bool isTransient, QueueOptions queueOpt, CancellationToken ct)
    {
        var failureAttempt = item.Attempt + 1;
        var action = QueueRetryPolicy.DecideFailureAction(isTransient, failureAttempt, item.MaxAttempts);

        await priceSnapshotRepository.InsertProductErrorAsync(
            runId,
            item.QueueId,
            item.Url,
            errorCode,
            httpStatus,
            message,
            ct);

        if (action == QueueFailureAction.Retry)
        {
            var jitterMax = Math.Max(1, queueOpt.RetryBaseDelayMs);
            var jitterMs = Random.Shared.Next(0, jitterMax);
            var delay = QueueRetryPolicy.ComputeBackoffDelay(
                failureAttempt,
                queueOpt.RetryBaseDelayMs,
                queueOpt.RetryMaxDelayMs,
                jitterMs);
            if (string.Equals(errorCode, CrawlerErrorCodes.TooManyRequests, StringComparison.OrdinalIgnoreCase))
            {
                var doubledMs = Math.Min(delay.TotalMilliseconds * 2d, Math.Max(queueOpt.RetryMaxDelayMs, 1));
                delay = TimeSpan.FromMilliseconds(doubledMs);
            }

            await queueRepository.MarkRetryAsync(item.QueueId, errorCode, httpStatus, message,
                DateTimeOffset.UtcNow.Add(delay), ct);
            return;
        }

        await queueRepository.MarkDeadAsync(item.QueueId, errorCode, httpStatus, message, ct);
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
}
