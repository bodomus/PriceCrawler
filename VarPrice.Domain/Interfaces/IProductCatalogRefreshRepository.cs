using VarPrice.Domain.Enums;
using VarPrice.Domain.Models;

namespace VarPrice.Domain.Interfaces;

public interface IProductCatalogRefreshRepository
{
    Task<long> StartAsync(
        string source,
        string discoverySource,
        DateTimeOffset startedAtUtc,
        TimeSpan runningTimeout,
        CancellationToken ct);

    Task CompleteAsync(
        long refreshId,
        ProductCatalogRefreshCompletion completion,
        CancellationToken ct);

    Task CompleteWithRunAsync(
        long refreshId,
        long runId,
        ProductCatalogRefreshCompletion completion,
        string? runNote,
        CancellationToken ct);

    Task FailAsync(
        long refreshId,
        string status,
        string errorCode,
        string? errorMessage,
        DateTimeOffset finishedAtUtc,
        CancellationToken ct);

    Task FailWithRunAsync(
        long refreshId,
        long runId,
        string status,
        string errorCode,
        string? errorMessage,
        DateTimeOffset finishedAtUtc,
        RunStatus runStatus,
        string? runNote,
        CancellationToken ct);

    Task<ProductCatalogRefreshSession?> GetByIdAsync(
        long refreshId,
        CancellationToken ct);
}
