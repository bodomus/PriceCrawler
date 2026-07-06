using PriceCrawler.Application.Grids.Runs.QueryRows;

namespace PriceCrawler.Application.Grids.Runs;

public interface IRunsGridQuerySource
{
    IQueryable<RunGridQueryRow> Build();
}
