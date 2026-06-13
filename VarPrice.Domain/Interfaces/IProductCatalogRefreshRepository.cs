using VarPrice.Domain.Models;

namespace VarPrice.Domain.Interfaces;

public interface IProductCatalogRefreshRepository
{
    Task<long> StartAsync(
        string source,
        string discoverySource,
        DateTimeOffset startedAtUtc,
        CancellationToken ct);

    Task CompleteAsync(
        long refreshId,
        ProductCatalogRefreshCompletion completion,
        CancellationToken ct);

    Task FailAsync(
        long refreshId,
        string status,
        string errorCode,
        string? errorMessage,
        DateTimeOffset finishedAtUtc,
        CancellationToken ct);

    Task<ProductCatalogRefreshSession?> GetByIdAsync(
        long refreshId,
        CancellationToken ct);
}
