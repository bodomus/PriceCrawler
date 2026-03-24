namespace VarPrice.Domain.Entities;

public sealed record PriceSnapshot(
    long Id,
    long RunId,
    long ProductId,
    decimal? Price,
    decimal? OldPrice,
    bool PromoFlag,
    bool InStock,
    long? QueueId,
    DateTimeOffset CapturedAt);
