using VarPrice.Domain.Enums;

namespace VarPrice.Domain.Interfaces;

public interface ICrawlerRunRepository
{
    Task<long> StartAsync(string source, CancellationToken ct);
    Task FinishAsync(long runId, RunStatus status, string? note, CancellationToken ct);
}
