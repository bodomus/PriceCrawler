using PriceCrawler.Domain.Models;

namespace PriceCrawler.Domain.Interfaces;

public interface IProductCatalogRepository
{
    Task<ProductCatalogUpsertResult> UpsertDiscoveredAsync(
        long refreshId,
        IReadOnlyCollection<ProductCatalogUpsertItem> items,
        CancellationToken ct);

    Task<int> GetActiveCountAsync(
        string source,
        CancellationToken ct);

    Task<int> DeactivateMissingAsync(
        string source,
        long currentRefreshId,
        DateTimeOffset notSeenSinceUtc,
        DateTimeOffset deactivatedAtUtc,
        CancellationToken ct);

    Task<IReadOnlyList<ProductCatalogItem>> GetDueProductsAsync(
        int limit,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        string workerId,
        CancellationToken ct);

    Task MarkCheckedAsync(
        ProductCatalogCheckSuccess success,
        CancellationToken ct);

    Task MarkFailedAsync(
        ProductCatalogCheckFailure failure,
        CancellationToken ct);

    Task<int> ReleaseReservationsAsync(
        IReadOnlyCollection<long> catalogItemIds,
        CancellationToken ct);

    Task<ProductCatalogItem?> GetByIdAsync(
        long id,
        CancellationToken ct);

    Task<ProductCatalogItem?> GetBySourceAndNormalizedUrlAsync(
        string source,
        string normalizedUrl,
        CancellationToken ct);
}
