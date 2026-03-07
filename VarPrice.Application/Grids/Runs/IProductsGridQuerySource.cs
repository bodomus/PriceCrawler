using VarPrice.Application.Grids.Runs.QueryRows;

namespace VarPrice.Application.Grids.Runs;

public interface IProductsGridQuerySource
{
    IQueryable<ProductGridQueryRow> Build(long snapshotId);
}
