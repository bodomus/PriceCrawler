namespace VarPrice.Infrastructure.Queries.Runs;

public sealed class ProductDetailsRow
{
    public long Id { get; init; }

    public long SnapshotId { get; init; }

    public long RunId { get; init; }

    public string? ExternalId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string? Slug { get; init; }

    public decimal? PackValue { get; init; }

    public string? PackUnit { get; init; }

    public decimal? CurrentPrice { get; init; }

    public decimal? OldPrice { get; init; }

    public decimal? DiscountPercent { get; init; }

    public bool PromoFlag { get; init; }

    public bool InStock { get; init; }

    public DateTime? UpdatedAtUtc { get; init; }

    public DateTime? CapturedAtUtc { get; init; }

    public string? Source { get; init; }

    public string? Brand { get; init; }

    public string? Category { get; init; }

    public string? ImageUrl { get; init; }
}
