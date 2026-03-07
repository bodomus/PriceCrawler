using Microsoft.EntityFrameworkCore;

using VarPrice.Application.Grids.Runs.QueryRows;
using VarPrice.Infrastructure.Persistence;

namespace VarPrice.Infrastructure.Queries.Runs;

public sealed class SnapshotsGridQuerySource(VarPriceDbContext dbContext) : ISnapshotsGridQuerySource
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
                City = snapshot.City,
                Price = snapshot.Price,
                OldPrice = snapshot.OldPrice,
                DiscountPercent = snapshot.OldPrice.HasValue && snapshot.OldPrice.Value > 0
                    ? ((snapshot.OldPrice.Value - snapshot.Price) / snapshot.OldPrice.Value) * 100m
                    : (decimal?)null,
                PromoFlag = snapshot.PromoFlag,
                InStock = snapshot.InStock
            });
    }
}
