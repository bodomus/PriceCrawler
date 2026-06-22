using VarPrice.Domain.Enums;
using VarPrice.Domain.Models;

namespace VarPrice.Domain.Interfaces;

public interface ICrawlerRunRepository
{
    Task<long> StartAsync(string source, CancellationToken ct);
    Task FinishAsync(long runId, RunStatus status, string? note, CancellationToken ct);

    Task<long> StartAsync(string runType, string source, string? discoverySource, CancellationToken ct);

    Task CompleteAsync(
        long runId,
        RunStatus status,
        CrawlerRunStatistics statistics,
        IReadOnlyCollection<CrawlerRunStageTiming> stageTimings,
        string? note,
        string? errorCode,
        string? errorMessage,
        CancellationToken ct);
}
