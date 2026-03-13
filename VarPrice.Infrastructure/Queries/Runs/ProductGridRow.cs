namespace VarPrice.Infrastructure.Queries.Runs;

public sealed class ProductGridRow
{
    public long ProductKey { get; init; }

    public string ProductId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public decimal? PackValue { get; init; }

    public string? PackUnit { get; init; }

    public DateTime? LastSeenAtUtc { get; init; }

    public decimal? SnapshotPrice { get; init; }
}
