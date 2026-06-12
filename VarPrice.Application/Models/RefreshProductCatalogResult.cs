namespace VarPrice.Application.Models;

public sealed record RefreshProductCatalogResult(
    long RunId,
    RefreshProductCatalogStatus Status,
    string Source,
    int DiscoveredCount,
    int AcceptedCount,
    int InsertedCount,
    int UpdatedCount,
    int SkippedCount,
    string? ErrorCode,
    string? Message);

public enum RefreshProductCatalogStatus
{
    Ok,
    Error
}
