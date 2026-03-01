using VarPrice.Domain.Enums;
using VarPrice.Domain.ValueObjects;

namespace VarPrice.Domain.Entities;

public sealed class IngestionRun
{
    public long IngestionRunId { get; private set; }
    public long CrawlerRunId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public RunStatus Status { get; private set; }
    public ErrorInfo? Error { get; private set; }

    public IngestionRun(long ingestionRunId, long crawlerRunId, DateTimeOffset startedAt, RunStatus status)
    {
        IngestionRunId = ingestionRunId;
        CrawlerRunId = crawlerRunId;
        StartedAt = startedAt;
        Status = status;
    }

    public void Complete()
    {
        Status = RunStatus.Ok;
        FinishedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(ErrorInfo error)
    {
        Error = error;
        Status = RunStatus.Error;
        FinishedAt = DateTimeOffset.UtcNow;
    }
}
