using Microsoft.EntityFrameworkCore;

using VarPrice.Application.Grids.Runs;
using VarPrice.Application.Grids.Runs.QueryRows;
using VarPrice.Infrastructure.Persistence;

namespace VarPrice.Infrastructure.Queries.Runs;

public sealed class ProductPriceHistoryQuerySource(VarPriceDbContext dbContext) : IProductPriceHistoryQuerySource
{
    public IQueryable<ProductPriceHistoryQueryRow> Build(long snapshotId)
    {
        return dbContext.Database
            .SqlQuery<ProductPriceHistoryRow>($"""
                                               select
                                                   h.id as "Id",
                                                   h.run_id as "RunId",
                                                   h.captured_at as "CapturedAtUtc",
                                                   h.price as "Price",
                                                   h.old_price as "OldPrice",
                                                   case
                                                       when h.old_price is not null
                                                           and h.old_price > 0
                                                           and h.price is not null
                                                           and h.old_price > h.price
                                                           then round(((h.old_price - h.price) / h.old_price) * 100, 1)
                                                       else null
                                                       end as "DiscountPercent",
                                                   h.promo_flag as "PromoFlag",
                                                   h.in_stock as "InStock",
                                                   coalesce(r.source, 'varus') as "Source"
                                               from price_snapshot h
                                               join price_snapshot selected_snapshot on selected_snapshot.id = {snapshotId}
                                               left join crawler_run r on r.id = h.run_id
                                               where h.product_id = selected_snapshot.product_id
                                               """)
            .Select(row => new ProductPriceHistoryQueryRow
            {
                Id = row.Id,
                RunId = row.RunId,
                CapturedAtUtc = row.CapturedAtUtc,
                Price = row.Price,
                OldPrice = row.OldPrice,
                DiscountPercent = row.DiscountPercent,
                PromoFlag = row.PromoFlag,
                InStock = row.InStock,
                Source = row.Source
            });
    }
}
