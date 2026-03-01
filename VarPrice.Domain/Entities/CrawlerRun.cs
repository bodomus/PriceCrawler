using VarPrice.Domain.Enums;

namespace VarPrice.Domain.Entities;

public sealed class CrawlerRun
{
    public long RunId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public RunStatus Status { get; private set; }
    public string Source { get; private set; }
    public string? Note { get; private set; }

    public CrawlerRun(long runId, DateTimeOffset startedAt, RunStatus status, string source)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Source is required", nameof(source));
        RunId = runId;
        StartedAt = startedAt;
        Status = status;
        Source = source;
    }

    public void Complete(string? note)
    {
        Status = RunStatus.Ok;
        Note = note;
        FinishedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string? note)
    {
        Status = RunStatus.Error;
        Note = note;
        FinishedAt = DateTimeOffset.UtcNow;
    }
}
