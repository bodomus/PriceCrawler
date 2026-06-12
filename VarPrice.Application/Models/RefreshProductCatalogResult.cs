namespace VarPrice.Application.Models;

public sealed record RefreshProductCatalogResult(
    long RunId,
    string Status,
    string Source,
    int DiscoveredCount,
    int AcceptedCount,
    int InsertedCount,
    int UpdatedCount,
    int SkippedCount,
    string? ErrorCode,
    string? Message);
