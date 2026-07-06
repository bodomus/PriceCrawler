using PriceCrawler.Application.Grids.Runs.QueryRows;

namespace PriceCrawler.Application.Grids.Runs;

public interface IProductDetailsQuerySource
{
    IQueryable<ProductDetailsQueryRow> Build(long snapshotId);
}
