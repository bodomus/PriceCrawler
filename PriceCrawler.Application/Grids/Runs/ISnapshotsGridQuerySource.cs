using PriceCrawler.Application.Grids.Runs.QueryRows;

namespace PriceCrawler.Application.Grids.Runs;

public interface ISnapshotsGridQuerySource
{
    IQueryable<SnapshotGridQueryRow> Build(long runId);
}
