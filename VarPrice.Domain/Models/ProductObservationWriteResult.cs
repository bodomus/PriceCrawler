namespace VarPrice.Domain.Models;

public sealed record ProductObservationWriteResult(
    long ProductId,
    long? PriceSnapshotId,
    bool SnapshotCreated,
    bool ProductCreated = false,
    bool ProductUpdated = false);
