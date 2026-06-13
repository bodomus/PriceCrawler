namespace VarPrice.Application.Models;

public sealed record RefreshProductCatalogResult(
    long RunId,
    long RefreshId,
    RefreshProductCatalogStatus Status,
    string Source,
    int DiscoveredCount,
    int AcceptedCount,
    int InsertedCount,
    int UpdatedCount,
    int ReactivatedCount,
    int DeactivatedCount,
    int SkippedCount,
    bool DeactivationExecuted,
    string? DeactivationSkipReason,
    string? ErrorCode,
    string? Message);

public enum RefreshProductCatalogStatus
{
    Ok,
    Error
}
