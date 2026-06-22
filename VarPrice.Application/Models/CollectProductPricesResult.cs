using VarPrice.Domain.Models;

namespace VarPrice.Application.Models;

public sealed record CollectProductPricesResult(
    long RunId,
    string Status,
    int SelectedCount,
    int EnqueuedCount,
    int SucceededCount,
    int FailedCount,
    int RetryCount,
    int DeadCount,
    string? ErrorCode,
    string? Message,
    int ProductsCreatedCount = 0,
    int ProductsUpdatedCount = 0,
    int SnapshotsCreatedCount = 0,
    int ErrorsCreatedCount = 0,
    long DurationMs = 0,
    CrawlerRunStatistics? Statistics = null,
    IReadOnlyList<CrawlerRunStageTiming>? StageTimings = null);
