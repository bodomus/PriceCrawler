namespace VarPrice.Domain.Models;

public sealed record ProductErrorRecord(
    long RunId,
    long? ProductKey,
    long? PriceSnapshotId,
    long? QueueId,
    DateTimeOffset OccurredAtUtc,
    string Stage,
    string ErrorCode,
    string ErrorMessage,
    string? DetailsJson);
