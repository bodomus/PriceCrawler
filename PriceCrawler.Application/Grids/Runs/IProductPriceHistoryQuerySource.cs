using PriceCrawler.Application.Grids.Runs.QueryRows;

namespace PriceCrawler.Application.Grids.Runs;

public interface IProductPriceHistoryQuerySource
{
    IQueryable<ProductPriceHistoryQueryRow> Build(long snapshotId);
}
