using PriceCrawler.Application.Grids.Runs.QueryRows;

namespace PriceCrawler.Application.Grids.Runs;

public interface IProductsGridQuerySource
{
    IQueryable<ProductGridQueryRow> Build(long snapshotId);
}
