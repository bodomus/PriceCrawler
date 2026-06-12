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
    string? Message);
