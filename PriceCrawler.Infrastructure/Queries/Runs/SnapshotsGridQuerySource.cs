using Microsoft.EntityFrameworkCore;

using PriceCrawler.Application.Grids.Runs.QueryRows;
using PriceCrawler.Infrastructure.Persistence;

namespace PriceCrawler.Infrastructure.Queries.Runs;

public sealed class SnapshotsGridQuerySource(PriceCrawlerDbContext dbContext) : ISnapshotsGridQuerySource
{
    public IQueryable<SnapshotGridQueryRow> Build(long runId)
    {
        return dbContext.PriceSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.RunId == runId)
            .Select(snapshot => new SnapshotGridQueryRow
            {
                Id = snapshot.Id,
                CapturedAtUtc = snapshot.CapturedAtUtc,
                Price = snapshot.Price,
                OldPrice = snapshot.OldPrice,
                DiscountPercent = snapshot.OldPrice.HasValue
                                  && snapshot.Price.HasValue
                                  && snapshot.OldPrice.Value > 0
                                  && snapshot.Price.Value < snapshot.OldPrice.Value
                    ? Math.Round(((snapshot.OldPrice.Value - snapshot.Price.Value) / snapshot.OldPrice.Value) * 100m, 0)
                    : null,
                PromoFlag = snapshot.PromoFlag,
                InStock = snapshot.InStock,
                IsSuccessful = !dbContext.CrawlErrors.Any(error =>
                    error.RunId == snapshot.RunId
                    && error.ProductId == snapshot.ProductId)
            });
    }
}
