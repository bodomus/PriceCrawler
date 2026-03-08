using VarPrice.Domain.Models;

namespace VarPrice.Domain.Interfaces;

public interface IPriceCollectQueueRepository
{
    Task<int> EnqueueAsync(long runId, IReadOnlyCollection<QueueEnqueueItem> items, int maxAttempts,
        CancellationToken ct);

    Task<IReadOnlyList<ReservedQueueItem>> ReserveBatchAsync(
        long runId,
        int batchSize,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken ct);

    Task MarkSucceededAsync(long queueId, CancellationToken ct);

    Task MarkRetryAsync(long queueId, string errorCode, int? httpStatus, string? message, DateTimeOffset nextAttemptAt,
        CancellationToken ct);

    Task MarkDeadAsync(long queueId, string errorCode, int? httpStatus, string? message, CancellationToken ct);

    Task<int> ReapExpiredReservationsAsync(long runId, CancellationToken ct);

    Task<bool> HasOutstandingItemsAsync(long runId, CancellationToken ct);

    Task<QueueRunStats> GetRunStatsAsync(long runId, CancellationToken ct);
}
