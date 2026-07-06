using PriceCrawler.Application.Grids.Runs.QueryRows;

namespace PriceCrawler.Application.Grids.Runs;

public interface IRunsTreeQuerySource
{
    IQueryable<RunTreeQueryRow> Build();
}
