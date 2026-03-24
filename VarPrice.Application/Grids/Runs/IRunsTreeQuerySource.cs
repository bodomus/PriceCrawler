using VarPrice.Application.Grids.Runs.QueryRows;

namespace VarPrice.Application.Grids.Runs;

public interface IRunsTreeQuerySource
{
    IQueryable<RunTreeQueryRow> Build();
}
