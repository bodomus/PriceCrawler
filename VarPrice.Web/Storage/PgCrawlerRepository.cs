using System.Data;
using System.Data.Common;
using VarPrice.Web.Storage.Db;

namespace VarPrice.Web.Storage;

public interface ICrawlerRepository
{
    Task<DbResult<long>> StartRunAsync(string source, CancellationToken ct);

    Task<DbResult> FinishRunAsync(long runId, string status, string? note, CancellationToken ct);

    Task<DbResult<long>> UpsertProductAsync(string productId, string name, string url, decimal? packValue, string? packUnit, CancellationToken ct);

    Task<DbResult> InsertSnapshotAsync(long runId, long productKey, string? city, decimal price, decimal? oldPrice, bool promoFlag, bool? inStock, CancellationToken ct);
}

public sealed class PgCrawlerRepository(IPgConnectionFactory factory, DbExecutor dbExecutor) : ICrawlerRepository
{
    public Task<DbResult<long>> StartRunAsync(string source, CancellationToken ct)
    {
        return dbExecutor.ExecuteAsync(
            async () =>
            {
                await using var cn = (DbConnection)factory.Create();
                await cn.OpenAsync(ct);

                await using var cmd = cn.CreateCommand();
                cmd.CommandText = @"
insert into Crawler_run(status, source) values ('running', @source)
returning run_id;";
                AddParam(cmd, "@source", source);
                var scalar = await cmd.ExecuteScalarAsync(ct);
                return Convert.ToInt64(scalar);
            },
            operation: $"{nameof(PgCrawlerRepository)}.{nameof(StartRunAsync)}",
            entity: "Crawler_run",
            parameters: new Dictionary<string, object?> { ["source"] = source });
    }

    public Task<DbResult> FinishRunAsync(long runId, string status, string? note, CancellationToken ct)
    {
        return dbExecutor.ExecuteAsync(
            async () =>
            {
                await using var cn = (DbConnection)factory.Create();
                await cn.OpenAsync(ct);

                await using var cmd = cn.CreateCommand();
                cmd.CommandText = @"
update Crawler_run
set status=@status, note=@note, finished_at=now()
where run_id=@runId;";
                AddParam(cmd, "@status", status);
                AddParam(cmd, "@note", note);
                AddParam(cmd, "@runId", runId);

                await cmd.ExecuteNonQueryAsync(ct);
            },
            operation: $"{nameof(PgCrawlerRepository)}.{nameof(FinishRunAsync)}",
            entity: "Crawler_run",
            entityId: runId.ToString(),
            parameters: new Dictionary<string, object?> { ["status"] = status });
    }

    public Task<DbResult<long>> UpsertProductAsync(string productId, string name, string url, decimal? packValue, string? packUnit, CancellationToken ct)
    {
        return dbExecutor.ExecuteAsync(
            async () =>
            {
                await using var cn = (DbConnection)factory.Create();
                await cn.OpenAsync(ct);

                await using var cmd = cn.CreateCommand();
                cmd.CommandText = @"
insert into product(product_id, name, url, pack_value, pack_unit)
values(@pid, @name, @url, @pv, @pu)
on conflict (product_id) do update
set name=excluded.name,
    url=excluded.url,
    pack_value=excluded.pack_value,
    pack_unit=excluded.pack_unit
returning product_key;";
                AddParam(cmd, "@pid", productId);
                AddParam(cmd, "@name", name);
                AddParam(cmd, "@url", url);
                AddParam(cmd, "@pv", packValue);
                AddParam(cmd, "@pu", packUnit);

                var obj = await cmd.ExecuteScalarAsync(ct);
                return Convert.ToInt64(obj);
            },
            operation: $"{nameof(PgCrawlerRepository)}.{nameof(UpsertProductAsync)}",
            entity: "product",
            entityId: productId,
            parameters: new Dictionary<string, object?>
            {
                ["productId"] = productId,
                ["name"] = name,
                ["url"] = url
            });
    }

    public Task<DbResult> InsertSnapshotAsync(long runId, long productKey, string? city, decimal price, decimal? oldPrice, bool promoFlag, bool? inStock, CancellationToken ct)
    {
        return dbExecutor.ExecuteAsync(
            async () =>
            {
                await using var cn = (DbConnection)factory.Create();
                await cn.OpenAsync(ct);

                await using var cmd = cn.CreateCommand();
                cmd.CommandText = @"
insert into price_snapshot(run_id, product_key, city, price, old_price, promo_flag, in_stock)
values(@runId, @pk, @city, @price, @old, @promo, @stock);";
                AddParam(cmd, "@runId", runId);
                AddParam(cmd, "@pk", productKey);
                AddParam(cmd, "@city", city);
                AddParam(cmd, "@price", price);
                AddParam(cmd, "@old", oldPrice);
                AddParam(cmd, "@promo", promoFlag);
                AddParam(cmd, "@stock", inStock);

                await cmd.ExecuteNonQueryAsync(ct);
            },
            operation: $"{nameof(PgCrawlerRepository)}.{nameof(InsertSnapshotAsync)}",
            entity: "price_snapshot",
            entityId: $"{runId}:{productKey}",
            parameters: new Dictionary<string, object?>
            {
                ["runId"] = runId,
                ["productKey"] = productKey,
                ["city"] = city,
                ["promoFlag"] = promoFlag,
                ["inStock"] = inStock
            });
    }

    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}
