namespace VarPrice.Domain.Models;

public static class ProductCatalogRefreshStatuses
{
    public const string Running = "running";
    public const string Ok = "ok";
    public const string Error = "error";
    public const string Cancelled = "cancelled";
}

public sealed record ProductCatalogRefreshSession(
    long Id,
    string Source,
    string DiscoverySource,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string Status,
    int DiscoveredCount,
    int AcceptedCount,
    int InsertedCount,
    int UpdatedCount,
    int DeactivatedCount,
    int ReactivatedCount,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record ProductCatalogRefreshCompletion(
    int DiscoveredCount,
    int AcceptedCount,
    int InsertedCount,
    int UpdatedCount,
    int DeactivatedCount,
    int ReactivatedCount,
    DateTimeOffset FinishedAtUtc);
