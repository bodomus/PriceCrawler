namespace VarPrice.Domain.Entities;

public sealed record PriceSnapshot(
    long SnapshotId,
    long RunId,
    long ProductKey,
    string? City,
    decimal? RegularPrice,
    decimal? FinalPrice,
    int? DiscountPercent,
    bool PromoFlag,
    bool? InStock,
    long? QueueId,
    DateTimeOffset CapturedAt);
