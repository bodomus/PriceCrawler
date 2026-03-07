using VarPrice.Application.Grids.Runs.QueryRows;

namespace VarPrice.Application.Grids.Runs;

public interface IRunsGridQuerySource
{
    IQueryable<RunGridQueryRow> Build();
}
