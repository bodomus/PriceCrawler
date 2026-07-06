namespace PriceCrawler.Domain.Models;

public sealed record CrawlerRunStatistics(
    int DiscoveredCount = 0,
    int AcceptedCount = 0,
    int InsertedCount = 0,
    int UpdatedCount = 0,
    int ReactivatedCount = 0,
    int DeactivatedCount = 0,
    int SelectedCount = 0,
    int EnqueuedCount = 0,
    int SucceededCount = 0,
    int RetryCount = 0,
    int DeadCount = 0,
    int FailedCount = 0,
    int ProductsCreatedCount = 0,
    int ProductsUpdatedCount = 0,
    int SnapshotsCreatedCount = 0,
    int ErrorsCreatedCount = 0);

public sealed record CrawlerRunStageTiming(string Stage, long DurationMs, int? ItemCount = null);

public sealed record CrawlerRunDetails(
    long Id,
    string RunType,
    string Source,
    string? DiscoverySource,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    long? DurationMs,
    CrawlerRunStatistics Statistics,
    IReadOnlyList<CrawlerRunStageTiming> StageTimings,
    string? ErrorCode,
    string? ErrorMessage,
    string? Note);

public sealed record CrawlerRunSummary(
    long Id,
    string RunType,
    string Source,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    long? DurationMs,
    int PrimaryCount,
    int SucceededCount,
    int FailedCount,
    string? ErrorCode);

public sealed record CrawlerRunAggregateStatistics(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? RunType,
    int TotalRuns,
    int SuccessfulRuns,
    int FailedRuns,
    long TotalDurationMs,
    double AverageDurationMs,
    long TotalDiscovered,
    long TotalAccepted,
    long TotalSelected,
    long TotalSucceeded,
    long TotalDead,
    long TotalSnapshotsCreated,
    long TotalErrorsCreated);
