using System.Data.Common;
using System.Text.Json;

using Npgsql;

using NpgsqlTypes;

using PriceCrawler.Domain.Categories;
using PriceCrawler.Domain.Interfaces;

namespace PriceCrawler.Infrastructure.Persistence;

/// <summary>PostgreSQL implementation of <see cref="ICategoryRepository"/> using set-based SQL.</summary>
/// <param name="connectionFactory">Creates connections to the configured database.</param>
public sealed class PgCategoryRepository(IPgConnectionFactory connectionFactory) : ICategoryRepository
{
    // Property names are written as declared (snake_case) to match jsonb_to_recordset columns.
    private static readonly JsonSerializerOptions JsonOptions = new();

    /// <inheritdoc />
    public async Task<CategorySyncResult> SyncTreeAsync(
        CategoryTreeSnapshot tree,
        long refreshId,
        DateTimeOffset seenAtUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (tree.Nodes.Count == 0)
        {
            throw new ArgumentException("An empty tree would deactivate every category; refusing to sync it.", nameof(tree));
        }

        var payload = JsonSerializer.Serialize(
            tree.Nodes.Select(node => new
            {
                external_id = node.ExternalId,
                parent_external_id = node.ParentExternalId,
                name = node.Name,
                url = node.Url,
                sort_order = node.SortOrder
            }),
            JsonOptions);

        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await ExecuteAsync(connection, transaction, """
            create temporary table tmp_category_input on commit drop as
            select x.external_id, x.parent_external_id, x.name, x.url, x.sort_order
            from jsonb_to_recordset(@items::jsonb)
                as x(external_id text, parent_external_id text, name text, url text, sort_order integer);
            """, ct, ("items", payload, NpgsqlDbType.Jsonb));

        // Counts are taken before the writes so they describe the change, not the result.
        var counts = await QuerySingleAsync(connection, transaction, """
            select
                count(*) filter (where c.id is null)::int as inserted,
                count(*) filter (where c.id is not null
                    and (c.name, c.url, c.sort_order) is distinct from (i.name, i.url, i.sort_order))::int as renamed,
                count(*) filter (where c.id is not null and p.external_id is distinct from i.parent_external_id)::int as moved,
                count(*) filter (where c.id is not null and not c.is_active)::int as reactivated
            from tmp_category_input i
            left join category c on c.source = @source and c.external_id = i.external_id
            left join category p on p.id = c.parent_id;
            """, reader => (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)),
            ct, ("source", tree.Source, NpgsqlDbType.Varchar));

        var refreshParameter = refreshId > 0 ? (object)refreshId : DBNull.Value;
        await ExecuteAsync(connection, transaction, """
            insert into category as c (source, external_id, name, url, sort_order, is_active,
                                       first_seen_at, last_seen_at, last_seen_refresh_id)
            select @source, i.external_id, i.name, i.url, i.sort_order, true, @seen_at, @seen_at, @refresh_id
            from tmp_category_input i
            on conflict (source, external_id) do update
            set name = excluded.name,
                url = excluded.url,
                sort_order = excluded.sort_order,
                is_active = true,
                deactivated_at = null,
                last_seen_at = excluded.last_seen_at,
                last_seen_refresh_id = excluded.last_seen_refresh_id,
                updated_at = case
                    when (c.name, c.url, c.sort_order, c.is_active) is distinct from
                         (excluded.name, excluded.url, excluded.sort_order, true) then now()
                    else c.updated_at end;

            update category c
            set parent_id = p.id,
                updated_at = now()
            from tmp_category_input i
            left join category p on p.source = @source and p.external_id = i.parent_external_id
            where c.source = @source
              and c.external_id = i.external_id
              and c.parent_id is distinct from p.id;
            """, ct,
            ("source", tree.Source, NpgsqlDbType.Varchar),
            ("seen_at", seenAtUtc.UtcDateTime, NpgsqlDbType.TimestampTz),
            ("refresh_id", refreshParameter, NpgsqlDbType.Bigint));

        var deactivated = await ExecuteAsync(connection, transaction, """
            update category c
            set is_active = false,
                deactivated_at = @seen_at,
                updated_at = now()
            where c.source = @source
              and c.is_active
              and not exists (select 1 from tmp_category_input i where i.external_id = c.external_id);
            """, ct,
            ("source", tree.Source, NpgsqlDbType.Varchar),
            ("seen_at", seenAtUtc.UtcDateTime, NpgsqlDbType.TimestampTz));

        await transaction.CommitAsync(ct);
        return new CategorySyncResult(
            tree.Nodes.Count,
            counts.Item1,
            counts.Item2,
            counts.Item3,
            counts.Item4,
            deactivated);
    }

    /// <inheritdoc />
    public async Task<int> GetActiveCountAsync(string source, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        return await QuerySingleAsync(connection, null,
            "select count(*)::int from category where source = @source and is_active;",
            reader => reader.GetInt32(0),
            ct, ("source", source, NpgsqlDbType.Varchar));
    }

    /// <inheritdoc />
    public async Task<CategoryLinkResult> LinkProductsAsync(
        string source,
        IReadOnlyCollection<CategoryProductListing> listings,
        long refreshId,
        DateTimeOffset seenAtUtc,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(listings);
        if (listings.Count == 0)
        {
            return new CategoryLinkResult(0, 0, 0);
        }

        var links = JsonSerializer.Serialize(
            listings.SelectMany(listing => listing.ProductUrls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => new { category_external_id = listing.CategoryExternalId, url = url.Trim() })),
            JsonOptions);
        var complete = listings
            .Where(listing => listing.IsComplete)
            .Select(listing => listing.CategoryExternalId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await ExecuteAsync(connection, transaction, """
            create temporary table tmp_category_link on commit drop as
            select distinct x.category_external_id, x.url
            from jsonb_to_recordset(@links::jsonb) as x(category_external_id text, url text);
            """, ct, ("links", links, NpgsqlDbType.Jsonb));

        var unknown = await QuerySingleAsync(connection, transaction, """
            select count(*)::int
            from tmp_category_link l
            where not exists (
                select 1 from product_catalog pc where pc.source = @source and pc.normalized_url = l.url);
            """, reader => reader.GetInt32(0), ct, ("source", source, NpgsqlDbType.Varchar));

        var refreshParameter = refreshId > 0 ? (object)refreshId : DBNull.Value;
        var linked = await ExecuteAsync(connection, transaction, """
            insert into product_catalog_category as pcc
                (product_catalog_id, category_id, first_seen_at, last_seen_at, last_seen_refresh_id)
            select pc.id, c.id, @seen_at, @seen_at, @refresh_id
            from tmp_category_link l
            join category c on c.source = @source and c.external_id = l.category_external_id
            join product_catalog pc on pc.source = @source and pc.normalized_url = l.url
            on conflict (product_catalog_id, category_id) do update
            set last_seen_at = excluded.last_seen_at,
                last_seen_refresh_id = excluded.last_seen_refresh_id;
            """, ct,
            ("source", source, NpgsqlDbType.Varchar),
            ("seen_at", seenAtUtc.UtcDateTime, NpgsqlDbType.TimestampTz),
            ("refresh_id", refreshParameter, NpgsqlDbType.Bigint));

        var removed = await ExecuteAsync(connection, transaction, """
            delete from product_catalog_category pcc
            using category c
            where pcc.category_id = c.id
              and c.source = @source
              and c.external_id = any(@complete)
              and pcc.last_seen_at < @seen_at;
            """, ct,
            ("source", source, NpgsqlDbType.Varchar),
            ("complete", complete, NpgsqlDbType.Array | NpgsqlDbType.Text),
            ("seen_at", seenAtUtc.UtcDateTime, NpgsqlDbType.TimestampTz));

        await transaction.CommitAsync(ct);
        return new CategoryLinkResult(linked, removed, unknown);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CategoryNode>> GetChildrenAsync(string source, long? parentId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            with recursive level as (
                select c.id, c.external_id, c.name, c.url, c.parent_id, c.sort_order
                from category c
                where c.source = @source
                  and c.is_active
                  and c.parent_id is not distinct from @parent_id
            ),
            subtree as (
                select l.id as root_id, l.id as category_id
                from level l
                union all
                select s.root_id, child.id
                from subtree s
                join category child on child.parent_id = s.category_id and child.is_active
            )
            select l.id, l.external_id, l.name, l.url, l.parent_id,
                   exists (select 1 from category child where child.parent_id = l.id and child.is_active) as has_children,
                   (select count(distinct pcc.product_catalog_id)::int
                    from subtree s
                    join product_catalog_category pcc on pcc.category_id = s.category_id
                    join product_catalog pc on pc.id = pcc.product_catalog_id and pc.is_active
                    where s.root_id = l.id) as product_count
            from level l
            order by l.sort_order, l.id;
            """;
        command.Parameters.Add(new NpgsqlParameter("source", NpgsqlDbType.Varchar) { Value = source });
        command.Parameters.Add(new NpgsqlParameter("parent_id", NpgsqlDbType.Bigint)
        {
            Value = parentId.HasValue ? parentId.Value : DBNull.Value
        });

        var nodes = new List<CategoryNode>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            nodes.Add(new CategoryNode(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.GetBoolean(5),
                reader.GetInt32(6)));
        }

        return nodes;
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = (NpgsqlConnection)connectionFactory.Create();
        await connection.OpenAsync(ct);
        return connection;
    }

    private static async Task<int> ExecuteAsync(
        NpgsqlConnection connection,
        DbTransaction? transaction,
        string sql,
        CancellationToken ct,
        params (string Name, object Value, NpgsqlDbType Type)[] parameters)
    {
        await using var command = CreateCommand(connection, transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<T> QuerySingleAsync<T>(
        NpgsqlConnection connection,
        DbTransaction? transaction,
        string sql,
        Func<DbDataReader, T> map,
        CancellationToken ct,
        params (string Name, object Value, NpgsqlDbType Type)[] parameters)
    {
        await using var command = CreateCommand(connection, transaction, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            throw new InvalidOperationException("Query returned no rows.");
        }

        return map(reader);
    }

    private static NpgsqlCommand CreateCommand(
        NpgsqlConnection connection,
        DbTransaction? transaction,
        string sql,
        (string Name, object Value, NpgsqlDbType Type)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, (NpgsqlTransaction?)transaction);
        foreach (var (name, value, type) in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value });
        }

        return command;
    }
}
