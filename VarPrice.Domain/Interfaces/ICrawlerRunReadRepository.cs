using VarPrice.Domain.Models;

namespace VarPrice.Domain.Interfaces;

public interface ICrawlerRunReadRepository
{
    Task<CrawlerRunDetails?> GetByIdAsync(long runId, CancellationToken ct);

    Task<IReadOnlyList<CrawlerRunSummary>> GetRecentAsync(int limit, string? runType, string? status,
        CancellationToken ct);

    Task<CrawlerRunAggregateStatistics> GetAggregateAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, string? runType,
        CancellationToken ct);
}
