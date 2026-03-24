namespace VarPrice.Application.Grids.Runs.QueryRows;

public sealed class RunTreeQueryRow
{
    public long Id { get; init; }

    public DateTime StartedAtUtc { get; init; }

    public DateTime? FinishedAtUtc { get; init; }

    public string Status { get; init; } = string.Empty;

    public int ItemsCount { get; init; }

    public int SuccessfulSnapshotsCount { get; init; }

    public int FailedSnapshotsCount { get; init; }
}
