namespace VarPrice.Application.Grids.Runs.QueryRows;

public sealed class ProductGridQueryRow
{
    public long Id { get; init; }

    public string? ExternalId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public decimal? PackValue { get; init; }

    public string? PackUnit { get; init; }

    public DateTime? UpdatedAtUtc { get; init; }

    public decimal? SnapshotPrice { get; init; }
}
