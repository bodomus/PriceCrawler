using VarPrice.Domain.Models;

namespace VarPrice.Domain.Interfaces;

public interface IProductCatalogRepository
{
    Task<ProductCatalogUpsertResult> UpsertDiscoveredAsync(
        IReadOnlyCollection<ProductCatalogUpsertItem> items,
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
