using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PriceCrawler.Application.Abstractions;
using PriceCrawler.Application.Models;
using PriceCrawler.Domain.Enums;
using PriceCrawler.Domain.Interfaces;
using PriceCrawler.Domain.Models;
using PriceCrawler.Domain.ValueObjects;

namespace PriceCrawler.Application.UseCases;

public sealed class RunCrawlerUseCase(
    IOptions<CrawlerOptions> options,
    IOptions<QueueOptions> queueOptions,
    IProductUrlDiscoveryService productUrlDiscoveryService,
    ICrawlerRunRepository crawlerRunRepository,
    IIngestionRunRepository ingestionRunRepository,
    IPriceCollectQueueRepository queueRepository,
    PriceCollectionQueueProcessor queueProcessor,
    ICrawlerProgressReporter progressReporter,
    ILogger<RunCrawlerUseCase> logger) : IRunCrawlerUseCase
{
    public async Task<CrawlerRunResult> RunVegetablesAsync(CancellationToken ct)
    {
        var opt = options.Value;
        var queueOpt = queueOptions.Value;
        ProductUrlDiscoveryResult discovery;

        progressReporter.Reset();

        try
        {
            progressReporter.SetCurrentStage("Обнаружение товаров");
            discovery = await productUrlDiscoveryService.DiscoverProductUrlsAsync(ct);
            progressReporter.SetTotalDiscovered(discovery.Urls.Count);
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
            var selectedUrls = discovery.Urls
                .Take(Math.Max(1, opt.MaxProductsPerRun))
                .ToList();
            var queueItems = selectedUrls
                .Select(url => new QueueEnqueueItem(url, BuildIdempotencyKey(runId, url)))
                .ToList();
            var enqueueResult = await queueRepository.EnqueueAsync(runId, queueItems, Math.Max(1, queueOpt.MaxAttempts), ct);
            progressReporter.SetNewProducts(enqueueResult.TotalAccepted);
            progressReporter.SetUpdatedProducts(Math.Max(0, queueItems.Count - enqueueResult.TotalAccepted));
            progressReporter.SetSelectedForCheck(enqueueResult.TotalAccepted);
            progressReporter.SetProductQueueTotal(enqueueResult.ProductAccepted);
            progressReporter.SetListingQueueTotal(enqueueResult.ListingAccepted);

            logger.LogInformation(
                "Queue seeded run_id={RunId} urls_total={UrlsTotal} enqueued={Enqueued} max_attempts={MaxAttempts}",
                runId,
                queueItems.Count,
                enqueueResult.TotalAccepted,
                Math.Max(1, queueOpt.MaxAttempts));

            progressReporter.SetCurrentStage("Проверка товаров");
            await queueProcessor.DrainQueueAsync(runId, opt, queueOpt, callbacks: null, ct);

            var stats = await queueRepository.GetRunStatsAsync(runId, ct);
            var runStatus = stats.Dead > 0 ? RunStatus.Error : RunStatus.Ok;
            var note =
                $"queued={queueItems.Count}, enqueued={enqueueResult.TotalAccepted}, succeeded={stats.Succeeded}, dead={stats.Dead}, pending={stats.Pending}, retry={stats.Retry}";
            logger.LogInformation("Crawler finished run_id={RunId} status={Status} {Note}", runId, runStatus, note);
            progressReporter.SetCurrentStage("Завершено");
            progressReporter.SetCurrentItem(string.Empty);

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
            progressReporter.SetCurrentStage("Ошибка");
            progressReporter.SetCurrentItem(string.Empty);
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
        progressReporter.SetCurrentStage("Ошибка обнаружения");
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

    private static string BuildIdempotencyKey(long runId, string url)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes($"{runId}:{url.Trim()}");
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
