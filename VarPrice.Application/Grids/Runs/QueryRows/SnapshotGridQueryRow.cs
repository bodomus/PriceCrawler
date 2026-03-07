namespace VarPrice.Application.Grids.Runs.QueryRows;

public sealed class SnapshotGridQueryRow
{
    public long Id { get; init; }

    public DateTime CapturedAtUtc { get; init; }

    public string? City { get; init; }

    public decimal Price { get; init; }

    public decimal? OldPrice { get; init; }

    public decimal? DiscountPercent { get; init; }

    public bool PromoFlag { get; init; }

    public bool? InStock { get; init; }
}
