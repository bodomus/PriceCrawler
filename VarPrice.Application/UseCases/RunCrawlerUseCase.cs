using System.Collections.Concurrent;

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

            var urlsCount = urls.Count;
            var maxConcurrency = Math.Max(1, opt.MaxConcurrency);
            var errorBreakdown = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrency,
                CancellationToken = ct
            };

            await Parallel.ForEachAsync(urls, parallelOptions, async (url, itemCt) =>
            {
                try
                {
                    var extractResult = await extractor.ExtractAsync(url, itemCt);
                    if (!extractResult.IsSuccess || extractResult.Card is null)
                    {
                        var errorCode = NormalizeErrorCode(extractResult.ErrorCode);
                        var message = TrimMessage(extractResult.Message);
                        Interlocked.Increment(ref errors);
                        errorBreakdown.AddOrUpdate(errorCode, 1, (_, value) => value + 1);

                        await priceSnapshotRepository.InsertProductErrorAsync(
                            runId,
                            url,
                            errorCode,
                            extractResult.HttpStatus,
                            message,
                            itemCt);

                        logger.LogWarning(
                            "Crawler failed {Url} error_code={ErrorCode} http_status={HttpStatus} latency_ms={LatencyMs} current_rps={CurrentRps:F2}",
                            url,
                            errorCode,
                            extractResult.HttpStatus,
                            extractResult.LatencyMs,
                            extractResult.ApproximateRps);
                        return;
                    }

                    var card = extractResult.Card;
                    var productKey = await priceSnapshotRepository.UpsertProductAsync(card.ProductId, card.Name,
                        card.Url, card.PackValue, card.PackUnit, itemCt);
                    await priceSnapshotRepository.InsertSnapshotAsync(runId, productKey, card.City, card.Price,
                        card.OldPrice, card.PromoFlag, card.InStock, itemCt);
                    var processedNow = Interlocked.Increment(ref processed);
                    var done = processedNow + Volatile.Read(ref errors);
                    logger.LogInformation(
                        "Crawler success {Done}/{Total} url={Url} sku={Sku} latency_ms={LatencyMs} http_status={HttpStatus} current_rps={CurrentRps:F2}",
                        done,
                        urlsCount,
                        url,
                        card.ProductId,
                        extractResult.LatencyMs,
                        extractResult.HttpStatus,
                        extractResult.ApproximateRps);
                }
                catch (OperationCanceledException) when (itemCt.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errors);
                    errorBreakdown.AddOrUpdate(CrawlerErrorCodes.Unknown, 1, (_, value) => value + 1);

                    var message = TrimMessage(ex.Message);
                    try
                    {
                        await priceSnapshotRepository.InsertProductErrorAsync(
                            runId,
                            url,
                            CrawlerErrorCodes.Unknown,
                            null,
                            message,
                            itemCt);
                    }
                    catch (Exception persistEx)
                    {
                        logger.LogWarning(persistEx, "Failed to persist crawler error for {Url}", url);
                    }

                    logger.LogWarning(ex, "Failed to ingest {Url}", url);
                }
            });

            var breakdown = errorBreakdown.Count == 0
                ? "none"
                : string.Join(", ", errorBreakdown.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}"));

            var note = $"processed={processed}, errors={errors}, breakdown={breakdown}";
            logger.LogInformation("Crawler finished run_id={RunId} {Note}", runId, note);
            await ingestionRunRepository.FinishAsync(ingestionRunId, RunStatus.Ok, null, ct);
            await crawlerRunRepository.FinishAsync(runId, RunStatus.Ok, note, ct);

            return new CrawlerRunResult(runId, RunStatus.Ok.ToString().ToLowerInvariant(), processed, errors, note);
        }
        catch (Exception ex)
        {
            var errorInfo = new ErrorInfo("crawler_failed", ex.Message);
            await ingestionRunRepository.FinishAsync(ingestionRunId, RunStatus.Error, errorInfo, ct);
            await crawlerRunRepository.FinishAsync(runId, RunStatus.Error, ex.Message, ct);
            return new CrawlerRunResult(runId, RunStatus.Error.ToString().ToLowerInvariant(), processed, errors + 1,
                ex.Message);
        }
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
