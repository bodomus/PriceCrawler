using VarPrice.Domain.Models;

namespace VarPrice.Domain.Interfaces;

public interface IProductCatalogRepository
{
    Task<ProductCatalogUpsertResult> UpsertDiscoveredAsync(
        IReadOnlyCollection<ProductCatalogUpsertItem> items,
        CancellationToken ct);

    Task<ProductCatalogItem?> GetByIdAsync(
        long id,
        CancellationToken ct);

    Task<ProductCatalogItem?> GetBySourceAndNormalizedUrlAsync(
        string source,
        string normalizedUrl,
        CancellationToken ct);
}
