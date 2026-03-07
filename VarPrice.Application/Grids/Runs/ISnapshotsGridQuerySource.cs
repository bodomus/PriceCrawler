using VarPrice.Application.Grids.Runs.QueryRows;

namespace VarPrice.Application.Grids.Runs;

public interface ISnapshotsGridQuerySource
{
    IQueryable<SnapshotGridQueryRow> Build(long runId);
}
