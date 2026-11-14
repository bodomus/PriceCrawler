using System.Data.Common;

using Npgsql;

using PriceCrawler.Application.Features.Listing;
using PriceCrawler.Application.Features.Products;
using PriceCrawler.Infrastructure.Persistence;
using PriceCrawler.Infrastructure.Queries.Listing;

namespace PriceCrawler.Infrastructure.Queries.Products;

/// <summary>
/// Reads product pages from PostgreSQL. A store's products are its active <c>product_catalog</c>
/// rows that have been observed at least once (<c>product.url = product_catalog.normalized_url</c>),
/// with the latest <c>price_snapshot</c> of each.
/// </summary>
/// <param name="connectionFactory">Creates database connections.</param>
public sealed class PgProductListReader(IPgConnectionFactory connectionFactory) : IProductListReader
{
    // Whitelist of sortable/filterable columns of the "products" CTE below.
    private static readonly Dictionary<string, string> Columns = new(StringComparer.OrdinalIgnoreCase)
    {
        [ProductListFields.Name.Name] = "name",
        [ProductListFields.ExternalId.Name] = "external_id",
        [ProductListFields.Price.Name] = "price",
        [ProductListFields.OldPrice.Name] = "old_price",
        [ProductListFields.IsPromo.Name] = "is_promo",
        [ProductListFields.InStock.Name] = "in_stock",
        [ProductListFields.CapturedAt.Name] = "captured_at",
    };

    /// <inheritdoc/>
    public async Task<PagedResult<ProductListItemDto>> ReadAsync(
        string store,
        long? categoryId,
        ListSpec spec,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(store);
        ArgumentNullException.ThrowIfNull(spec);

        var builder = new SqlListBuilder(Columns);
        string where = builder.BuildWhere(spec.Filters);
        string orderBy = builder.BuildOrderBy(spec.Sort, "product_id");
        string products = BuildProductsCte(categoryId.HasValue);

        await using var connection = (NpgsqlConnection)connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        long total;
        await using (NpgsqlCommand count = connection.CreateCommand())
        {
            count.CommandText = $"{products} select count(*) from products where {where};";
            AddCommonParameters(count, store, categoryId, builder);
            total = (long)(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        }

        List<ProductListItemDto> items = [];
        if (total > spec.Offset)
        {
            await using NpgsqlCommand page = connection.CreateCommand();
            page.CommandText = $"""
                {products}
                select product_id, external_id, name, url, pack_value, pack_unit,
                       price, old_price, is_promo, in_stock, captured_at
                from products
                where {where}
                order by {orderBy}
                limit @limit offset @offset;
                """;
            AddCommonParameters(page, store, categoryId, builder);
            page.Parameters.AddWithValue("limit", spec.PageSize);
            page.Parameters.AddWithValue("offset", spec.Offset);

            await using NpgsqlDataReader reader = await page.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(Map(reader));
            }
        }

        return new PagedResult<ProductListItemDto>(items, spec.Page, spec.PageSize, total);
    }

    /// <summary>Builds the CTE that defines the product rows of a store, optionally limited to a category subtree.</summary>
    /// <param name="byCategory">Whether to limit to the subtree of <c>@category_id</c>.</param>
    /// <returns>SQL starting with <c>with</c>.</returns>
    private static string BuildProductsCte(bool byCategory)
    {
        string scope = byCategory
            ? """
              scope as (
                  select c.id from category c
                  where c.id = @category_id and c.source = @store and c.is_active
                  union all
                  select child.id from category child join scope on child.parent_id = scope.id
                  where child.is_active
              ),
              """
            : string.Empty;
        string inScope = byCategory
            ? """
              and exists (
                  select 1 from product_catalog_category pcc
                  join scope on scope.id = pcc.category_id
                  where pcc.product_catalog_id = pc.id)
              """
            : string.Empty;

        return $"""
            with recursive {scope}
            products as (
                select p.id as product_id, p.external_id, p.name, p.url, p.pack_value, p.pack_unit,
                       latest.price, latest.old_price,
                       coalesce(latest.promo_flag, false) as is_promo,
                       coalesce(latest.in_stock, false) as in_stock,
                       latest.captured_at
                from product_catalog pc
                join product p on p.url = pc.normalized_url
                left join lateral (
                    select s.price, s.old_price, s.promo_flag, s.in_stock, s.captured_at
                    from price_snapshot s
                    where s.product_id = p.id
                    order by s.captured_at desc, s.id desc
                    limit 1
                ) latest on true
                where pc.source = @store
                  and pc.is_active
                  {inScope}
            )
            """;
    }

    /// <summary>Adds the store, category and filter parameters to a command.</summary>
    /// <param name="command">Command to fill.</param>
    /// <param name="store">Store key.</param>
    /// <param name="categoryId">Category id, when filtering by category.</param>
    /// <param name="builder">Builder holding the filter parameters.</param>
    private static void AddCommonParameters(NpgsqlCommand command, string store, long? categoryId, SqlListBuilder builder)
    {
        command.Parameters.AddWithValue("store", store);
        if (categoryId.HasValue)
        {
            command.Parameters.AddWithValue("category_id", categoryId.Value);
        }

        builder.AddTo(command.Parameters);
    }

    /// <summary>Maps the current row.</summary>
    /// <param name="reader">Reader positioned on a row.</param>
    /// <returns>The product.</returns>
    private static ProductListItemDto Map(DbDataReader reader) => new(
        reader.GetInt64(0),
        reader.IsDBNull(1) ? null : reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetDecimal(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetDecimal(6),
        reader.IsDBNull(7) ? null : reader.GetDecimal(7),
        reader.GetBoolean(8),
        reader.GetBoolean(9),
        reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10));
}
