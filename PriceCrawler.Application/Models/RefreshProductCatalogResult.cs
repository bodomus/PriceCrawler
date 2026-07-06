using PriceCrawler.Domain.Models;

namespace PriceCrawler.Application.Models;

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
    string? Message,
    long DurationMs = 0,
    CrawlerRunStatistics? Statistics = null,
    IReadOnlyList<CrawlerRunStageTiming>? StageTimings = null);

public enum RefreshProductCatalogStatus
{
    Ok,
    Error
}
