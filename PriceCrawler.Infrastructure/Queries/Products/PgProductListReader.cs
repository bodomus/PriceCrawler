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
/// with the latest <c>price_snapshot</c> of each, the previous Kyiv day's closing price and, for the
/// rows of the page, the daily closing prices of the last <see cref="TrendDays"/> days.
/// </summary>
/// <param name="connectionFactory">Creates database connections.</param>
public sealed class PgProductListReader(IPgConnectionFactory connectionFactory) : IProductListReader
{
    /// <summary>Days of history in <see cref="ProductListItemDto.Trend"/>.</summary>
    public const int TrendDays = 30;

    /// <summary>Time zone that defines a "day" for the day's change and the trend.</summary>
    public const string DayTimeZone = "Europe/Kyiv";

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
        [ProductListFields.PriceChange.Name] = "price_change",
        [ProductListFields.PriceChangePercent.Name] = "price_change_percent",
    };

    /// <inheritdoc/>
    public async Task<ProductPageDto> ReadAsync(ProductListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Store);
        ListSpec spec = request.Spec;

        var builder = new SqlListBuilder(Columns);
        string where = builder.BuildWhere(spec.Filters);
        string orderBy = builder.BuildOrderBy(spec.Sort, "product_id");
        if (request.Grouping != ProductGrouping.None)
        {
            orderBy = $"group_order, group_key, {orderBy}";
        }

        string products = BuildProductsCte(request.CategoryId.HasValue, request.Grouping);

        await using var connection = (NpgsqlConnection)connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        List<ProductGroupDto> groups = [];
        long total = 0;
        await using (NpgsqlCommand count = connection.CreateCommand())
        {
            count.CommandText = request.Grouping == ProductGrouping.None
                ? $"{products} select null, null, count(*) from products where {where};"
                : $"""
                  {products}
                  select group_key, min(group_name), count(*)
                  from products where {where}
                  group by group_order, group_key
                  order by group_order, group_key;
                  """;
            AddCommonParameters(count, request, builder);
            await using NpgsqlDataReader reader = await count.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                long groupCount = reader.GetInt64(2);
                total += groupCount;
                if (request.Grouping != ProductGrouping.None)
                {
                    groups.Add(new ProductGroupDto(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), groupCount));
                }
            }
        }

        List<ProductRow> rows = [];
        if (total > spec.Offset)
        {
            await using NpgsqlCommand page = connection.CreateCommand();
            page.CommandText = $"""
                {products}
                select product_id, external_id, name, url, pack_value, pack_unit,
                       price, old_price, is_promo, in_stock, captured_at,
                       previous_price, price_change, price_change_percent, group_key
                from products
                where {where}
                order by {orderBy}
                limit @limit offset @offset;
                """;
            AddCommonParameters(page, request, builder);
            page.Parameters.AddWithValue("limit", spec.PageSize);
            page.Parameters.AddWithValue("offset", spec.Offset);

            await using NpgsqlDataReader reader = await page.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(Map(reader));
            }
        }

        Dictionary<long, List<decimal>> trends = await ReadTrendsAsync(
            connection, [.. rows.Select(row => row.ProductId)], cancellationToken).ConfigureAwait(false);

        List<ProductListItemDto> items =
            [.. rows.Select(row => row.ToDto(trends.TryGetValue(row.ProductId, out List<decimal>? trend) ? trend : []))];
        return new ProductPageDto(items, spec.Page, spec.PageSize, total, groups);
    }

    /// <summary>
    /// Reads the daily closing prices for some products: the last <see cref="TrendDays"/> Kyiv days up to each
    /// product's latest observation (so a product that is no longer collected still shows its last month).
    /// </summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="productIds">Products of the page.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Prices per product, oldest day first.</returns>
    private static async Task<Dictionary<long, List<decimal>>> ReadTrendsAsync(
        NpgsqlConnection connection,
        long[] productIds,
        CancellationToken cancellationToken)
    {
        Dictionary<long, List<decimal>> trends = [];
        if (productIds.Length == 0)
        {
            return trends;
        }

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            select product_id, price
            from (
                select s.product_id, s.price, (s.captured_at at time zone @tz)::date as day,
                       row_number() over (
                           partition by s.product_id, (s.captured_at at time zone @tz)::date
                           order by s.captured_at desc, s.id desc) as rn
                from (
                    select product_id, max(captured_at) as last_at
                    from price_snapshot
                    where product_id = any(@ids)
                    group by product_id
                ) last
                join price_snapshot s on s.product_id = last.product_id
                where s.price is not null
                  and s.captured_at >= (date_trunc('day', last.last_at at time zone @tz) - make_interval(days => @days - 1)) at time zone @tz
            ) daily
            where rn = 1
            order by product_id, day;
            """;
        command.Parameters.AddWithValue("ids", productIds);
        command.Parameters.AddWithValue("tz", DayTimeZone);
        command.Parameters.AddWithValue("days", TrendDays);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            long id = reader.GetInt64(0);
            if (!trends.TryGetValue(id, out List<decimal>? prices))
            {
                prices = [];
                trends[id] = prices;
            }

            prices.Add(reader.GetDecimal(1));
        }

        return trends;
    }

    /// <summary>
    /// Builds the CTE that defines the product rows of a store, optionally limited to a category subtree,
    /// with the previous day's price and the group columns.
    /// </summary>
    /// <param name="byCategory">Whether to limit to the subtree of <c>@category_id</c>.</param>
    /// <param name="grouping">Grouping that defines <c>group_key</c>, <c>group_name</c> and <c>group_order</c>.</param>
    /// <returns>SQL starting with <c>with</c>.</returns>
    private static string BuildProductsCte(bool byCategory, ProductGrouping grouping)
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

        // Category groups: every active category under the requested one (or every top-level one)
        // is mapped to its "branch" - the direct child (top-level category) it belongs to.
        string branches = grouping == ProductGrouping.Category
            ? $"""
              branches as (
                  select c.id as branch_id, c.name as branch_name, c.sort_order as branch_order, c.id as category_id
                  from category c
                  where c.source = @store and c.is_active
                    and {(byCategory ? "c.parent_id = @category_id" : "c.parent_id is null")}
                  union all
                  select b.branch_id, b.branch_name, b.branch_order, child.id
                  from category child join branches b on child.parent_id = b.category_id
                  where child.is_active
              ),
              """
            : string.Empty;
        string branchJoin = grouping == ProductGrouping.Category
            ? """
              left join lateral (
                  select b.branch_id, b.branch_name, b.branch_order
                  from product_catalog_category pcc
                  join branches b on b.category_id = pcc.category_id
                  where pcc.product_catalog_id = pc.id
                  order by b.branch_order, b.branch_id
                  limit 1
              ) branch on true
              """
            : string.Empty;

        (string key, string name, string order) = grouping switch
        {
            ProductGrouping.Category => (
                $"coalesce(branch.branch_id::text, '{ProductGroupings.NoSubcategoryKey}')",
                "branch.branch_name",
                "case when branch.branch_id is null then 2147483647 else branch.branch_order end"),
            ProductGrouping.Promo => (
                $"case when coalesce(latest.promo_flag, false) then '{ProductGroupings.PromoKey}' else '{ProductGroupings.RegularKey}' end",
                "null::text",
                "case when coalesce(latest.promo_flag, false) then 0 else 1 end"),
            ProductGrouping.Stock => (
                $"case when coalesce(latest.in_stock, false) then '{ProductGroupings.InStockKey}' else '{ProductGroupings.OutOfStockKey}' end",
                "null::text",
                "case when coalesce(latest.in_stock, false) then 0 else 1 end"),
            ProductGrouping.Change => (
                $"""
                case when latest.price is null or prev.price is null then '{ProductGroupings.UnknownKey}'
                     when latest.price < prev.price then '{ProductGroupings.DownKey}'
                     when latest.price > prev.price then '{ProductGroupings.UpKey}'
                     else '{ProductGroupings.SameKey}' end
                """,
                "null::text",
                """
                case when latest.price is null or prev.price is null then 3
                     when latest.price < prev.price then 0
                     when latest.price > prev.price then 1
                     else 2 end
                """),
            _ => ("null::text", "null::text", "0"),
        };

        return $"""
            with recursive {scope}
            {branches}
            products as (
                select p.id as product_id, p.external_id, p.name, p.url, p.pack_value, p.pack_unit,
                       latest.price, latest.old_price,
                       coalesce(latest.promo_flag, false) as is_promo,
                       coalesce(latest.in_stock, false) as in_stock,
                       latest.captured_at,
                       prev.price as previous_price,
                       latest.price - prev.price as price_change,
                       case when prev.price > 0 then round((latest.price - prev.price) * 100 / prev.price, 2) end
                           as price_change_percent,
                       {key} as group_key,
                       {name} as group_name,
                       {order} as group_order
                from product_catalog pc
                join product p on p.url = pc.normalized_url
                left join lateral (
                    select s.price, s.old_price, s.promo_flag, s.in_stock, s.captured_at
                    from price_snapshot s
                    where s.product_id = p.id
                    order by s.captured_at desc, s.id desc
                    limit 1
                ) latest on true
                left join lateral (
                    select s.price
                    from price_snapshot s
                    where s.product_id = p.id
                      and s.price is not null
                      and s.captured_at < (date_trunc('day', latest.captured_at at time zone @tz) at time zone @tz)
                    order by s.captured_at desc, s.id desc
                    limit 1
                ) prev on true
                {branchJoin}
                where pc.source = @store
                  and pc.is_active
                  {inScope}
            )
            """;
    }

    /// <summary>Adds the store, time zone, category and filter parameters to a command.</summary>
    /// <param name="command">Command to fill.</param>
    /// <param name="request">The list request.</param>
    /// <param name="builder">Builder holding the filter parameters.</param>
    private static void AddCommonParameters(NpgsqlCommand command, ProductListRequest request, SqlListBuilder builder)
    {
        command.Parameters.AddWithValue("store", request.Store);
        command.Parameters.AddWithValue("tz", DayTimeZone);
        if (request.CategoryId.HasValue)
        {
            command.Parameters.AddWithValue("category_id", request.CategoryId.Value);
        }

        builder.AddTo(command.Parameters);
    }

    /// <summary>Maps the current row.</summary>
    /// <param name="reader">Reader positioned on a row.</param>
    /// <returns>The row without its trend.</returns>
    private static ProductRow Map(DbDataReader reader) => new(
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
        reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
        reader.IsDBNull(11) ? null : reader.GetDecimal(11),
        reader.IsDBNull(12) ? null : reader.GetDecimal(12),
        reader.IsDBNull(13) ? null : reader.GetDecimal(13),
        reader.IsDBNull(14) ? null : reader.GetString(14));

    /// <summary>A page row before its trend is attached.</summary>
    private sealed record ProductRow(
        long ProductId,
        string? ExternalId,
        string Name,
        string Url,
        decimal? PackValue,
        string? PackUnit,
        decimal? Price,
        decimal? OldPrice,
        bool IsPromo,
        bool InStock,
        DateTimeOffset? CapturedAt,
        decimal? PreviousPrice,
        decimal? PriceChange,
        decimal? PriceChangePercent,
        string? GroupKey)
    {
        /// <summary>Builds the DTO with its trend.</summary>
        /// <param name="trend">Daily closing prices, oldest first.</param>
        /// <returns>The list item.</returns>
        public ProductListItemDto ToDto(IReadOnlyList<decimal> trend) => new(
            ProductId, ExternalId, Name, Url, PackValue, PackUnit, Price, OldPrice, IsPromo, InStock, CapturedAt,
            PreviousPrice, PriceChange, PriceChangePercent, trend, GroupKey);
    }
}
