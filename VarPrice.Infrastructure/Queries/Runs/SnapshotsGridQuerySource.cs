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
                FinalPrice = snapshot.FinalPrice,
                RegularPrice = snapshot.RegularPrice,
                DiscountPercent = snapshot.DiscountPercent,
                PromoFlag = snapshot.PromoFlag,
                InStock = snapshot.InStock,
                IsSuccessful = !dbContext.ProductErrors.Any(error => error.PriceSnapshotId == snapshot.Id)
            });
    }
}
