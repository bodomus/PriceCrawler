namespace VarPrice.Domain.Models;

public sealed record ProductObservationWriteResult(
    long ProductKey,
    long? SnapshotId,
    bool SnapshotCreated);
