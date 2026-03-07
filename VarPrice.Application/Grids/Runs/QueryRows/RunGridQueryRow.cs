namespace VarPrice.Application.Grids.Runs.QueryRows;

public sealed class RunGridQueryRow
{
    public long Id { get; init; }

    public DateTime StartedAtUtc { get; init; }

    public DateTime? FinishedAtUtc { get; init; }

    public string Status { get; init; } = string.Empty;

    public int ItemsCount { get; init; }
}
