using VarPrice.Application.Grids.Runs.QueryRows;

namespace VarPrice.Application.Grids.Runs;

public interface IProductDetailsQuerySource
{
    IQueryable<ProductDetailsQueryRow> Build(long snapshotId);
}
