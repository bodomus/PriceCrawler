namespace VarPrice.Infrastructure.Queries.Runs;

public sealed class ProductPriceHistoryRow
{
    public long Id { get; init; }

    public long RunId { get; init; }

    public DateTime CapturedAtUtc { get; init; }

    public decimal? Price { get; init; }

    public decimal? OldPrice { get; init; }

    public decimal? DiscountPercent { get; init; }

    public bool PromoFlag { get; init; }

    public bool InStock { get; init; }

    public string Source { get; init; } = string.Empty;
}
