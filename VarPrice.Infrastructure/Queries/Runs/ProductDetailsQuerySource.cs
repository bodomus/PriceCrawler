using Microsoft.EntityFrameworkCore;

using VarPrice.Application.Grids.Runs;
using VarPrice.Application.Grids.Runs.QueryRows;
using VarPrice.Infrastructure.Persistence;

namespace VarPrice.Infrastructure.Queries.Runs;

public sealed class ProductDetailsQuerySource(VarPriceDbContext dbContext) : IProductDetailsQuerySource
{
    public IQueryable<ProductDetailsQueryRow> Build(long snapshotId)
    {
        return dbContext.Database
            .SqlQuery<ProductDetailsRow>($"""
                                          select
                                              p.id as "Id",
                                              s.id as "SnapshotId",
                                              s.run_id as "RunId",
                                              p.external_id as "ExternalId",
                                              p.name as "Name",
                                              p.url as "Url",
                                              p.slug as "Slug",
                                              p.pack_value as "PackValue",
                                              p.pack_unit as "PackUnit",
                                              s.price as "CurrentPrice",
                                              s.old_price as "OldPrice",
                                              case
                                                  when s.old_price is not null
                                                      and s.old_price > 0
                                                      and s.price is not null
                                                      and s.old_price > s.price
                                                      then round(((s.old_price - s.price) / s.old_price) * 100, 1)
                                                  else null
                                                  end as "DiscountPercent",
                                              s.promo_flag as "PromoFlag",
                                              s.in_stock as "InStock",
                                              p.updated_at as "UpdatedAtUtc",
                                              s.captured_at as "CapturedAtUtc",
                                              coalesce(r.source, 'varus') as "Source",
                                              null::text as "Brand",
                                              null::text as "Category",
                                              null::text as "ImageUrl"
                                          from price_snapshot s
                                          join product p on p.id = s.product_id
                                          left join crawler_run r on r.id = s.run_id
                                          where s.id = {snapshotId}
                                          """)
            .Select(row => new ProductDetailsQueryRow
            {
                Id = row.Id,
                SnapshotId = row.SnapshotId,
                RunId = row.RunId,
                ExternalId = row.ExternalId,
                Name = row.Name,
                Url = row.Url,
                Slug = row.Slug,
                PackValue = row.PackValue,
                PackUnit = row.PackUnit,
                CurrentPrice = row.CurrentPrice,
                OldPrice = row.OldPrice,
                DiscountPercent = row.DiscountPercent,
                PromoFlag = row.PromoFlag,
                InStock = row.InStock,
                UpdatedAtUtc = row.UpdatedAtUtc,
                CapturedAtUtc = row.CapturedAtUtc,
                Source = row.Source,
                Brand = row.Brand,
                Category = row.Category,
                ImageUrl = row.ImageUrl
            });
    }
}
