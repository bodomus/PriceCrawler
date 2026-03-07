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
                                           p.product_key as "ProductKey",
                                           p.name as "Name",
                                           p.product_id as "ProductId",
                                           p.url as "Url",
                                           p.pack_value as "PackValue",
                                           p.pack_unit as "PackUnit",
                                           p.created_at as "CreatedAtUtc",
                                           s.price as "SnapshotPrice"
                                       from price_snapshot s
                                       join product p on p.product_key = s.product_key
                                       where s.snapshot_id = {snapshotId}
                                       """)
            .Select(row => new ProductGridQueryRow
            {
                ProductKey = row.ProductKey,
                ProductId = row.ProductId,
                Name = row.Name,
                Url = row.Url,
                PackValue = row.PackValue,
                PackUnit = row.PackUnit,
                CreatedAtUtc = row.CreatedAtUtc,
                SnapshotPrice = row.SnapshotPrice
            });
    }
}
