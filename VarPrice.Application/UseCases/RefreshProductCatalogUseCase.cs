using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;
using VarPrice.Domain.Enums;
using VarPrice.Domain.Interfaces;
using VarPrice.Domain.Models;

namespace VarPrice.Application.UseCases;

public sealed class RefreshProductCatalogUseCase(
    IProductUrlDiscoveryService productUrlDiscoveryService,
    IProductCatalogRepository productCatalogRepository,
    IProductCatalogRefreshRepository refreshRepository,
    ICrawlerRunRepository crawlerRunRepository,
    IOptions<CrawlerOptions> crawlerOptions,
    ILogger<RefreshProductCatalogUseCase> logger) : IRefreshProductCatalogUseCase
{
    private const string CatalogRunSource = "catalog-refresh";
    private const string ProductCatalogSource = "varus";

    public async Task<RefreshProductCatalogResult> ExecuteAsync(CancellationToken ct)
    {
        var options = crawlerOptions.Value;
        var runId = await crawlerRunRepository.StartAsync(CatalogRunSource, ct);
        var discoverySource = ToConfiguredDiscoverySource(options.DiscoveryMode);
        var refreshId = 0L;
        var discoveredCount = 0;
        var acceptedCount = 0;
        var insertedCount = 0;
        var updatedCount = 0;
        var reactivatedCount = 0;
        var deactivatedCount = 0;
        var skippedCount = 0;

        logger.LogInformation("Product catalog refresh started. RunId={RunId}", runId);

        try
        {
            refreshId = await refreshRepository.StartAsync(
                ProductCatalogSource,
                discoverySource,
                DateTimeOffset.UtcNow,
                GetRunningTimeout(options),
                ct);

            if (refreshId <= 0)
            {
                return await FinishFailureAsync(
                    runId,
                    refreshId,
                    "catalog_refresh_already_running",
                    discoverySource,
                    discoveredCount,
                    acceptedCount,
                    insertedCount,
                    updatedCount,
                    reactivatedCount,
                    deactivatedCount,
                    skippedCount,
                    "catalog refresh already running",
                    null,
                    ct);
            }

            logger.LogInformation(
                "Product catalog refresh session started. RunId={RunId}; RefreshId={RefreshId}; Source={Source}; DiscoverySource={DiscoverySource}",
                runId,
                refreshId,
                ProductCatalogSource,
                discoverySource);

            var activeCountBefore = await productCatalogRepository.GetActiveCountAsync(ProductCatalogSource, ct);

            ProductUrlDiscoveryResult discovery;
            try
            {
                discovery = await productUrlDiscoveryService.DiscoverProductUrlsAsync(ct);
            }
            catch (ProductUrlDiscoveryUnavailableException ex)
            {
                return await FinishFailureAsync(
                    runId,
                    refreshId,
                    CrawlerErrorCodes.ProductUrlDiscoveryUnavailable,
                    discoverySource,
                    discoveredCount,
                    acceptedCount,
                    insertedCount,
                    updatedCount,
                    reactivatedCount,
                    deactivatedCount,
                    skippedCount,
                    "product URL discovery unavailable",
                    ex,
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await FinishFailureAsync(
                    runId,
                    refreshId,
                    "catalog_discovery_failed",
                    discoverySource,
                    discoveredCount,
                    acceptedCount,
                    insertedCount,
                    updatedCount,
                    reactivatedCount,
                    deactivatedCount,
                    skippedCount,
                    $"catalog discovery failed: {TrimMessage(ex.Message)}",
                    ex,
                    ct);
            }

            discoveredCount = discovery.Urls.Count;
            try
            {
                discoverySource = ToDiscoverySource(discovery.SourceKind);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return await FinishFailureAsync(
                    runId,
                    refreshId,
                    "catalog_discovery_source_unsupported",
                    discoverySource,
                    discoveredCount,
                    acceptedCount,
                    insertedCount,
                    updatedCount,
                    reactivatedCount,
                    deactivatedCount,
                    Math.Max(0, discoveredCount),
                    $"unsupported discovery source: {discovery.SourceKind}",
                    ex,
                    ct);
            }

            logger.LogInformation(
                "Product catalog discovery completed. RunId={RunId}; RefreshId={RefreshId}; Discovered={Discovered}",
                runId,
                refreshId,
                discoveredCount);

            var discoveredAt = DateTimeOffset.UtcNow;
            var items = discovery.Urls
                .Select(url => url.Trim())
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => new ProductCatalogUpsertItem(
                    ProductCatalogSource,
                    url,
                    url,
                    null,
                    TryExtractSlug(url),
                    discoveredAt))
                .ToList();

            ProductCatalogUpsertResult upsertResult;
            try
            {
                upsertResult = await productCatalogRepository.UpsertDiscoveredAsync(refreshId, items, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await FinishFailureAsync(
                    runId,
                    refreshId,
                    "catalog_upsert_failed",
                    discoverySource,
                    discoveredCount,
                    acceptedCount,
                    insertedCount,
                    updatedCount,
                    reactivatedCount,
                    deactivatedCount,
                    Math.Max(0, discoveredCount),
                    $"catalog upsert failed: {TrimMessage(ex.Message)}",
                    ex,
                    ct);
            }

            acceptedCount = upsertResult.ReceivedCount;
            insertedCount = upsertResult.InsertedCount;
            updatedCount = upsertResult.UpdatedCount;
            reactivatedCount = upsertResult.ReactivatedCount;
            skippedCount = Math.Max(0, discoveredCount - acceptedCount);

            var safety = CatalogRefreshSafetyPolicy.Evaluate(new CatalogRefreshSafetyInput(
                discoverySource,
                acceptedCount,
                activeCountBefore,
                options));

            logger.LogInformation(
                "Product catalog refresh safety check. RefreshId={RefreshId}; ActiveBefore={ActiveBefore}; Accepted={Accepted}; MinimumExpected={MinimumExpected}; MinimumRatio={MinimumRatio}; IsSafe={IsSafe}",
                refreshId,
                activeCountBefore,
                acceptedCount,
                CatalogRefreshSafetyPolicy.NormalizeMinimumExpected(options.CatalogMinimumExpectedUrls),
                CatalogRefreshSafetyPolicy.NormalizePreviousRatio(options.CatalogMinimumPreviousRatio),
                !safety.IsError);

            if (safety.IsError)
            {
                return await FinishFailureAsync(
                    runId,
                    refreshId,
                    safety.ErrorCode ?? "catalog_refresh_safety_failed",
                    discoverySource,
                    discoveredCount,
                    acceptedCount,
                    insertedCount,
                    updatedCount,
                    reactivatedCount,
                    deactivatedCount,
                    skippedCount,
                    safety.Reason ?? "catalog refresh safety check failed",
                    null,
                    ct);
            }

            if (safety.CanDeactivate)
            {
                var deactivatedAt = DateTimeOffset.UtcNow;
                var cutoff = deactivatedAt.AddDays(-CatalogRefreshSafetyPolicy.NormalizeGracePeriodDays(
                    options.CatalogMissingGracePeriodDays));

                try
                {
                    deactivatedCount = await productCatalogRepository.DeactivateMissingAsync(
                        ProductCatalogSource,
                        refreshId,
                        cutoff,
                        deactivatedAt,
                        ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return await FinishFailureAsync(
                        runId,
                        refreshId,
                        "catalog_deactivation_failed",
                        discoverySource,
                        discoveredCount,
                        acceptedCount,
                        insertedCount,
                        updatedCount,
                        reactivatedCount,
                        deactivatedCount,
                        skippedCount,
                        $"catalog deactivation failed: {TrimMessage(ex.Message)}",
                        ex,
                        ct);
                }
            }
            else
            {
                logger.LogInformation(
                    "Product catalog deactivation skipped. RefreshId={RefreshId}; Reason={Reason}",
                    refreshId,
                    safety.Reason);
            }

            var note = BuildNote(
                discoverySource,
                refreshId,
                discoveredCount,
                acceptedCount,
                insertedCount,
                updatedCount,
                reactivatedCount,
                deactivatedCount,
                skippedCount,
                safety.CanDeactivate,
                safety.Reason,
                null);

            try
            {
                await refreshRepository.CompleteWithRunAsync(
                    refreshId,
                    runId,
                    new ProductCatalogRefreshCompletion(
                        discoveredCount,
                        acceptedCount,
                        insertedCount,
                        updatedCount,
                        deactivatedCount,
                        reactivatedCount,
                        DateTimeOffset.UtcNow),
                    note,
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await FinishFailureAsync(
                    runId,
                    refreshId,
                    "catalog_refresh_finalize_failed",
                    discoverySource,
                    discoveredCount,
                    acceptedCount,
                    insertedCount,
                    updatedCount,
                    reactivatedCount,
                    deactivatedCount,
                    skippedCount,
                    $"catalog refresh finalization failed: {TrimMessage(ex.Message)}",
                    ex,
                    CancellationToken.None);
            }

            logger.LogInformation(
                "Product catalog refresh completed. RunId={RunId}; RefreshId={RefreshId}; Inserted={Inserted}; Updated={Updated}; Reactivated={Reactivated}; Deactivated={Deactivated}",
                runId,
                refreshId,
                insertedCount,
                updatedCount,
                reactivatedCount,
                deactivatedCount);

            return new RefreshProductCatalogResult(
                runId,
                refreshId,
                RefreshProductCatalogStatus.Ok,
                discoverySource,
                discoveredCount,
                acceptedCount,
                insertedCount,
                updatedCount,
                reactivatedCount,
                deactivatedCount,
                skippedCount,
                safety.CanDeactivate,
                safety.Reason,
                null,
                note);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (refreshId > 0)
            {
                await TryFailRefreshAndRunAsync(
                    refreshId,
                    runId,
                    ProductCatalogRefreshStatuses.Cancelled,
                    "catalog_refresh_cancelled",
                    "catalog refresh cancelled",
                    "catalog refresh cancelled");
            }
            else
            {
                await TryFinishFailedRunAsync(runId, "catalog refresh cancelled");
            }

            throw;
        }
    }

    private async Task<RefreshProductCatalogResult> FinishFailureAsync(
        long runId,
        long refreshId,
        string errorCode,
        string discoverySource,
        int discoveredCount,
        int acceptedCount,
        int insertedCount,
        int updatedCount,
        int reactivatedCount,
        int deactivatedCount,
        int skippedCount,
        string message,
        Exception? exception,
        CancellationToken ct)
    {
        var note = BuildNote(
            discoverySource,
            refreshId,
            discoveredCount,
            acceptedCount,
            insertedCount,
            updatedCount,
            reactivatedCount,
            deactivatedCount,
            skippedCount,
            false,
            errorCode,
            errorCode);

        try
        {
            if (refreshId > 0)
            {
                await refreshRepository.FailWithRunAsync(
                    refreshId,
                    runId,
                    ProductCatalogRefreshStatuses.Error,
                    errorCode,
                    TrimMessage(message),
                    DateTimeOffset.UtcNow,
                    RunStatus.Error,
                    note,
                    ct);
            }
            else
            {
                await crawlerRunRepository.FinishAsync(runId, RunStatus.Error, note, ct);
            }
        }
        catch (Exception finishException)
        {
            logger.LogError(
                finishException,
                "Product catalog refresh failed to finish crawler run. RunId={RunId}; ErrorCode={ErrorCode}",
                runId,
                errorCode);
        }

        if (exception is null)
        {
            logger.LogWarning(
                "Product catalog refresh failed. RunId={RunId}; RefreshId={RefreshId}; ErrorCode={ErrorCode}",
                runId,
                refreshId,
                errorCode);
        }
        else
        {
            logger.LogError(
                exception,
                "Product catalog refresh failed. RunId={RunId}; RefreshId={RefreshId}; ErrorCode={ErrorCode}",
                runId,
                refreshId,
                errorCode);
        }

        return new RefreshProductCatalogResult(
            runId,
            refreshId,
            RefreshProductCatalogStatus.Error,
            discoverySource,
            discoveredCount,
            acceptedCount,
            insertedCount,
            updatedCount,
            reactivatedCount,
            deactivatedCount,
            skippedCount,
            false,
            errorCode,
            errorCode,
            note);
    }

    private async Task TryFailRefreshAndRunAsync(
        long refreshId,
        long runId,
        string status,
        string errorCode,
        string? errorMessage,
        string? runNote)
    {
        try
        {
            await refreshRepository.FailWithRunAsync(
                refreshId,
                runId,
                status,
                errorCode,
                TrimMessage(errorMessage),
                DateTimeOffset.UtcNow,
                RunStatus.Error,
                runNote,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Product catalog refresh failed to atomically update refresh session and crawler run. RunId={RunId}; RefreshId={RefreshId}; ErrorCode={ErrorCode}",
                runId,
                refreshId,
                errorCode);
        }
    }

    private async Task TryFinishFailedRunAsync(long runId, string note)
    {
        try
        {
            await crawlerRunRepository.FinishAsync(runId, RunStatus.Error, note, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Product catalog refresh cancellation cleanup failed. RunId={RunId}", runId);
        }
    }

    private static string BuildNote(
        string discoverySource,
        long refreshId,
        int discoveredCount,
        int acceptedCount,
        int insertedCount,
        int updatedCount,
        int reactivatedCount,
        int deactivatedCount,
        int skippedCount,
        bool deactivationExecuted,
        string? deactivationSkipReason,
        string? errorCode) =>
        $"refresh_id={refreshId}, discovery_source={discoverySource}, discovered={discoveredCount}, accepted={acceptedCount}, inserted={insertedCount}, updated={updatedCount}, reactivated={reactivatedCount}, deactivated={deactivatedCount}, skipped={skippedCount}, deactivation_executed={deactivationExecuted.ToString().ToLowerInvariant()}, deactivation_skip_reason={deactivationSkipReason ?? ""}, error_code={errorCode ?? ""}";

    private static string ToConfiguredDiscoverySource(string? discoveryMode) =>
        string.Equals(discoveryMode, ProductUrlDiscoveryModes.Api, StringComparison.OrdinalIgnoreCase)
            ? "api"
            : string.Equals(discoveryMode, ProductUrlDiscoveryModes.Sitemap, StringComparison.OrdinalIgnoreCase)
                ? "sitemap"
                : "category-seed";

    private static TimeSpan GetRunningTimeout(CrawlerOptions options) =>
        TimeSpan.FromMinutes(Math.Max(1, options.CatalogRefreshRunningTimeoutMinutes));

    private static string ToDiscoverySource(ProductUrlDiscoverySourceKind sourceKind) =>
        sourceKind switch
        {
            ProductUrlDiscoverySourceKind.CategorySeed => "category-seed",
            ProductUrlDiscoverySourceKind.Api => "api",
            ProductUrlDiscoverySourceKind.Sitemap => "sitemap",
            _ => throw new ArgumentOutOfRangeException(
                nameof(sourceKind),
                sourceKind,
                "Unsupported discovery source.")
        };

    private static string? TryExtractSlug(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Segments
            .Select(segment => segment.Trim('/'))
            .LastOrDefault(segment => !string.IsNullOrWhiteSpace(segment));
    }

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
