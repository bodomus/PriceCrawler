using Microsoft.Extensions.Logging;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;
using VarPrice.Domain.Enums;
using VarPrice.Domain.Interfaces;
using VarPrice.Domain.Models;

namespace VarPrice.Application.UseCases;

public sealed class RefreshProductCatalogUseCase(
    IProductUrlDiscoveryService productUrlDiscoveryService,
    IProductCatalogRepository productCatalogRepository,
    ICrawlerRunRepository crawlerRunRepository,
    ILogger<RefreshProductCatalogUseCase> logger) : IRefreshProductCatalogUseCase
{
    private const string CatalogRunSource = "catalog-refresh";
    private const string ProductCatalogSource = "varus";

    public async Task<RefreshProductCatalogResult> ExecuteAsync(CancellationToken ct)
    {
        var runId = await crawlerRunRepository.StartAsync(CatalogRunSource, ct);
        var discoverySource = "discovery";
        var discoveredCount = 0;

        logger.LogInformation("Product catalog refresh started. RunId={RunId}", runId);

        ProductUrlDiscoveryResult discovery;
        try
        {
            discovery = await productUrlDiscoveryService.DiscoverProductUrlsAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await TryFinishFailedRunAsync(runId, "catalog refresh cancelled");
            throw;
        }
        catch (ProductUrlDiscoveryUnavailableException ex)
        {
            return await FinishFailureAsync(
                runId,
                CrawlerErrorCodes.ProductUrlDiscoveryUnavailable,
                discoverySource,
                0,
                "product URL discovery unavailable",
                ex,
                ct);
        }
        catch (Exception ex)
        {
            return await FinishFailureAsync(
                runId,
                "catalog_discovery_failed",
                discoverySource,
                0,
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
                "catalog_discovery_source_unsupported",
                discoverySource,
                discoveredCount,
                $"unsupported discovery source: {discovery.SourceKind}",
                ex,
                ct);
        }

        logger.LogInformation(
            "Product catalog discovery completed. RunId={RunId}; DiscoverySource={DiscoverySource}; Discovered={Discovered}",
            runId,
            discoverySource,
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
            upsertResult = await productCatalogRepository.UpsertDiscoveredAsync(items, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await TryFinishFailedRunAsync(runId, "catalog refresh cancelled");
            throw;
        }
        catch (Exception ex)
        {
            return await FinishFailureAsync(
                runId,
                "catalog_upsert_failed",
                discoverySource,
                discoveredCount,
                $"catalog upsert failed: {TrimMessage(ex.Message)}",
                ex,
                ct);
        }

        var acceptedCount = upsertResult.ReceivedCount;
        var skippedCount = Math.Max(0, discoveredCount - acceptedCount);
        var note = BuildSuccessNote(
            discoverySource,
            discoveredCount,
            acceptedCount,
            upsertResult.InsertedCount,
            upsertResult.UpdatedCount,
            skippedCount);

        try
        {
            await crawlerRunRepository.FinishAsync(runId, RunStatus.Ok, note, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await TryFinishFailedRunAsync(runId, "catalog refresh cancelled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Product catalog refresh failed to finish successful crawler run. RunId={RunId}; DiscoverySource={DiscoverySource}; Discovered={Discovered}; Accepted={Accepted}; Inserted={Inserted}; Updated={Updated}; Skipped={Skipped}",
                runId,
                discoverySource,
                discoveredCount,
                acceptedCount,
                upsertResult.InsertedCount,
                upsertResult.UpdatedCount,
                skippedCount);

            var failureNote =
                $"{note}, error_code=catalog_run_finish_failed, message={TrimMessage(ex.Message)}";
            return new RefreshProductCatalogResult(
                runId,
                RefreshProductCatalogStatus.Error,
                discoverySource,
                discoveredCount,
                acceptedCount,
                upsertResult.InsertedCount,
                upsertResult.UpdatedCount,
                skippedCount,
                "catalog_run_finish_failed",
                failureNote);
        }

        logger.LogInformation(
            "Product catalog refresh completed. RunId={RunId}; DiscoverySource={DiscoverySource}; Discovered={Discovered}; Accepted={Accepted}; Inserted={Inserted}; Updated={Updated}; Skipped={Skipped}",
            runId,
            discoverySource,
            discoveredCount,
            acceptedCount,
            upsertResult.InsertedCount,
            upsertResult.UpdatedCount,
            skippedCount);

        return new RefreshProductCatalogResult(
            runId,
            RefreshProductCatalogStatus.Ok,
            discoverySource,
            discoveredCount,
            acceptedCount,
            upsertResult.InsertedCount,
            upsertResult.UpdatedCount,
            skippedCount,
            null,
            note);
    }

    private async Task<RefreshProductCatalogResult> FinishFailureAsync(
        long runId,
        string errorCode,
        string discoverySource,
        int discoveredCount,
        string message,
        Exception exception,
        CancellationToken ct)
    {
        var note =
            $"discovery_source={discoverySource}, discovered={discoveredCount}, error_code={errorCode}, message={TrimMessage(message)}";
        try
        {
            await crawlerRunRepository.FinishAsync(runId, RunStatus.Error, note, ct);
        }
        catch (Exception finishException)
        {
            logger.LogError(
                finishException,
                "Product catalog refresh failed to finish crawler run. RunId={RunId}; ErrorCode={ErrorCode}",
                runId,
                errorCode);
        }

        logger.LogError(
            exception,
            "Product catalog refresh failed. RunId={RunId}; ErrorCode={ErrorCode}; DiscoverySource={DiscoverySource}; Discovered={Discovered}",
            runId,
            errorCode,
            discoverySource,
            discoveredCount);

        return new RefreshProductCatalogResult(
            runId,
            RefreshProductCatalogStatus.Error,
            discoverySource,
            discoveredCount,
            0,
            0,
            0,
            Math.Max(0, discoveredCount),
            errorCode,
            note);
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

    private static string BuildSuccessNote(
        string discoverySource,
        int discoveredCount,
        int acceptedCount,
        int insertedCount,
        int updatedCount,
        int skippedCount) =>
        $"discovery_source={discoverySource}, discovered={discoveredCount}, accepted={acceptedCount}, inserted={insertedCount}, updated={updatedCount}, skipped={skippedCount}";

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
