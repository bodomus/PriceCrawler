using VarPrice.Domain.Enums;
using VarPrice.Domain.ValueObjects;

namespace VarPrice.Domain.Interfaces;

public interface IIngestionRunRepository
{
    Task<long> StartAsync(long crawlerRunId, CancellationToken ct);
    Task FinishAsync(long ingestionRunId, RunStatus status, ErrorInfo? errorInfo, CancellationToken ct);
}
