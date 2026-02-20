namespace VarPrice.Domain.Entities;

public sealed record PriceSnapshot(
    long SnapshotId,
    long RunId,
    long ProductKey,
    string? City,
    decimal Price,
    decimal? OldPrice,
    bool PromoFlag,
    bool? InStock,
    DateTimeOffset CapturedAt);
