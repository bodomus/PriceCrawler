using System.Data.Common;

using VarPrice.Application.Models;
using VarPrice.Domain.Enums;
using VarPrice.Domain.Interfaces;
using VarPrice.Domain.ValueObjects;

namespace VarPrice.Infrastructure.Persistence;

public sealed class PgCrawlerRunRepository(IPgConnectionFactory factory) : ICrawlerRunRepository
{
    public async Task<long> StartAsync(string source, CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "insert into crawler_run(status, source) values (@status, @source) returning run_id;";
        AddParam(cmd, "@status", ToStorage(RunStatus.Running));
        AddParam(cmd, "@source", source);
        var scalar = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(scalar);
    }

    public async Task FinishAsync(long runId, RunStatus status, string? note, CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "update crawler_run set status=@status, note=@note, finished_at=now() where run_id=@run_id;";
        AddParam(cmd, "@status", ToStorage(status));
        AddParam(cmd, "@note", note);
        AddParam(cmd, "@run_id", runId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string ToStorage(RunStatus status) => status switch
    {
        RunStatus.Running => "running",
        RunStatus.Ok => "ok",
        _ => "failed"
    };

    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}

public sealed class PgIngestionRunRepository(IPgConnectionFactory factory) : IIngestionRunRepository
{
    public async Task<long> StartAsync(long crawlerRunId, CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);

        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            "insert into ingestion_run(crawler_run_id, status) values (@crawler_run_id, @status) returning ingestion_run_id;";
        AddParam(cmd, "@crawler_run_id", crawlerRunId);
        AddParam(cmd, "@status", "running");
        var scalar = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(scalar);
    }

    public async Task FinishAsync(long ingestionRunId, RunStatus status, ErrorInfo? errorInfo, CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);

        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            @"update ingestion_run set status=@status, error_code=@error_code, error_message=@error_message, finished_at=now()
where ingestion_run_id=@ingestion_run_id;";
        AddParam(cmd, "@status", status == RunStatus.Ok ? "ok" : "failed");
        AddParam(cmd, "@error_code", errorInfo?.Code);
        AddParam(cmd, "@error_message", errorInfo?.Message);
        AddParam(cmd, "@ingestion_run_id", ingestionRunId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}

public sealed class PgPriceSnapshotRepository(IPgConnectionFactory factory) : IPriceSnapshotRepository
{
    public async Task<long> UpsertProductAsync(string productId, string name, string url, decimal? packValue,
        string? packUnit, CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = @"insert into product(product_id, name, url, pack_value, pack_unit)
values(@pid, @name, @url, @pv, @pu)
on conflict (product_id) do update set
name=excluded.name, url=excluded.url, pack_value=excluded.pack_value, pack_unit=excluded.pack_unit
returning product_key;";
        AddParam(cmd, "@pid", productId);
        AddParam(cmd, "@name", name);
        AddParam(cmd, "@url", url);
        AddParam(cmd, "@pv", packValue);
        AddParam(cmd, "@pu", packUnit);
        var scalar = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(scalar);
    }

    public async Task InsertSnapshotAsync(long runId, long productKey, string? city, decimal price, decimal? oldPrice,
        bool promoFlag, bool? inStock, CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);

        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            @"insert into price_snapshot(run_id, product_key, city, price, old_price, promo_flag, in_stock)
values(@run_id, @product_key, @city, @price, @old_price, @promo_flag, @in_stock);";
        AddParam(cmd, "@run_id", runId);
        AddParam(cmd, "@product_key", productKey);
        AddParam(cmd, "@city", city);
        AddParam(cmd, "@price", price);
        AddParam(cmd, "@old_price", oldPrice);
        AddParam(cmd, "@promo_flag", promoFlag);
        AddParam(cmd, "@in_stock", inStock);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task InsertProductErrorAsync(long runId, long? productKey, string? city, decimal price,
        decimal? oldPrice,
        bool promoFlag, bool? inStock, CancellationToken ct)
        => await InsertProductErrorAsync(
            runId,
            city ?? string.Empty,
            CrawlerErrorCodes.Unknown,
            null,
            "Legacy product error overload invoked",
            ct);

    public async Task InsertProductErrorAsync(long runId, string url, string errorCode, int? httpStatus,
        string? message, CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);

        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            @"insert into product_errors(run_id, product_id, name, url, pack_value, pack_unit, error_string, error_code, http_status, error_message)
values(@run_id, @product_id, @name, @url, @pack_value, @pack_unit, @error_string, @error_code, @http_status, @error_message);";
        var normalizedCode = string.IsNullOrWhiteSpace(errorCode)
            ? CrawlerErrorCodes.Unknown
            : errorCode.Trim().ToLowerInvariant();
        var normalizedUrl = Truncate(url, 1024);
        var normalizedMessage = Truncate(message, 512);
        var shortError = Truncate($"{normalizedCode}: {normalizedMessage}", 256);

        AddParam(cmd, "@run_id", runId);
        AddParam(cmd, "@product_id", DBNull.Value);
        AddParam(cmd, "@name", Truncate("crawler_error", 512));
        AddParam(cmd, "@url", normalizedUrl);
        AddParam(cmd, "@pack_value", DBNull.Value);
        AddParam(cmd, "@pack_unit", DBNull.Value);
        AddParam(cmd, "@error_string", shortError);
        AddParam(cmd, "@error_code", Truncate(normalizedCode, 64));
        AddParam(cmd, "@http_status", httpStatus);
        AddParam(cmd, "@error_message", normalizedMessage);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}
