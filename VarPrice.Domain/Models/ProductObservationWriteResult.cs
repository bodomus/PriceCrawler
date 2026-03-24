namespace VarPrice.Domain.Models;

public sealed record ProductObservationWriteResult(
    long ProductId,
    long? PriceSnapshotId,
    bool SnapshotCreated);
