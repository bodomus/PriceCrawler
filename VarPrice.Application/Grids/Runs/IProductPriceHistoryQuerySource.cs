using VarPrice.Application.Grids.Runs.QueryRows;

namespace VarPrice.Application.Grids.Runs;

public interface IProductPriceHistoryQuerySource
{
    IQueryable<ProductPriceHistoryQueryRow> Build(long snapshotId);
}
