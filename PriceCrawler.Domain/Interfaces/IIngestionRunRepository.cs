using PriceCrawler.Domain.Enums;
using PriceCrawler.Domain.ValueObjects;

namespace PriceCrawler.Domain.Interfaces;

public interface IIngestionRunRepository
{
    Task<long> StartAsync(long crawlerRunId, CancellationToken ct);
    Task FinishAsync(long ingestionRunId, RunStatus status, ErrorInfo? errorInfo, CancellationToken ct);
}
