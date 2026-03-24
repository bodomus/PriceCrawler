using Microsoft.EntityFrameworkCore;

using VarPrice.Application.Grids.Runs.QueryRows;
using VarPrice.Infrastructure.Persistence;

namespace VarPrice.Infrastructure.Queries.Runs;

public sealed class ProductsGridQuerySource(VarPriceDbContext dbContext) : IProductsGridQuerySource
{
    public IQueryable<ProductGridQueryRow> Build(long snapshotId)
    {
        return dbContext.Database
            .SqlQuery<ProductGridRow>($"""
                                       select
                                           p.id as "Id",
                                           p.name as "Name",
                                           p.external_id as "ExternalId",
                                           p.url as "Url",
                                           p.pack_value as "PackValue",
                                           p.pack_unit as "PackUnit",
                                           p.updated_at as "UpdatedAtUtc",
                                           coalesce(s.price, s.old_price) as "SnapshotPrice"
                                       from price_snapshot s
                                       join product p on p.id = s.product_id
                                       where s.id = {snapshotId}
                                       """)
            .Select(row => new ProductGridQueryRow
            {
                Id = row.Id,
                ExternalId = row.ExternalId,
                Name = row.Name,
                Url = row.Url,
                PackValue = row.PackValue,
                PackUnit = row.PackUnit,
                UpdatedAtUtc = row.UpdatedAtUtc,
                SnapshotPrice = row.SnapshotPrice
            });
    }
}
