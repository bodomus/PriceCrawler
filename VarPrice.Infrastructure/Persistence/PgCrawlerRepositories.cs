using System.Data.Common;

using VarPrice.Application.Models;
using VarPrice.Domain.Constants;
using VarPrice.Domain.Enums;
using VarPrice.Domain.Interfaces;
using VarPrice.Domain.Models;
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
        bool promoFlag, bool? inStock, long? queueId, CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);

        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            @"insert into price_snapshot(queue_id, run_id, product_key, city, price, old_price, promo_flag, in_stock)
values(@queue_id, @run_id, @product_key, @city, @price, @old_price, @promo_flag, @in_stock)
on conflict (queue_id) do update set
captured_at=now(),
run_id=excluded.run_id,
product_key=excluded.product_key,
city=excluded.city,
price=excluded.price,
old_price=excluded.old_price,
promo_flag=excluded.promo_flag,
in_stock=excluded.in_stock;";
        AddParam(cmd, "@queue_id", queueId);
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
            null,
            city ?? string.Empty,
            CrawlerErrorCodes.Unknown,
            null,
            "Legacy product error overload invoked",
            ct);

    public async Task InsertProductErrorAsync(long runId, string url, string errorCode, int? httpStatus,
        string? message, CancellationToken ct)
        => await InsertProductErrorAsync(runId, null, url, errorCode, httpStatus, message, ct);

    public async Task InsertProductErrorAsync(long runId, long? queueId, string url, string errorCode, int? httpStatus,
        string? message, CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);

        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            @"insert into product_errors(queue_id, run_id, product_id, name, url, pack_value, pack_unit, error_string, error_code, http_status, error_message)
values(@queue_id, @run_id, @product_id, @name, @url, @pack_value, @pack_unit, @error_string, @error_code, @http_status, @error_message)
on conflict (queue_id) do update set
created_at=now(),
run_id=excluded.run_id,
error_string=excluded.error_string,
error_code=excluded.error_code,
http_status=excluded.http_status,
error_message=excluded.error_message,
url=excluded.url;";
        var normalizedCode = string.IsNullOrWhiteSpace(errorCode)
            ? CrawlerErrorCodes.Unknown
            : errorCode.Trim().ToLowerInvariant();
        var normalizedUrl = Truncate(url, 1024);
        var normalizedMessage = Truncate(message, 512);
        var shortError = Truncate($"{normalizedCode}: {normalizedMessage}", 256);

        AddParam(cmd, "@queue_id", queueId);
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

public sealed class PgPriceCollectQueueRepository(IPgConnectionFactory factory) : IPriceCollectQueueRepository
{
    public async Task<int> EnqueueAsync(long runId, IReadOnlyCollection<QueueEnqueueItem> items, int maxAttempts,
        CancellationToken ct)
    {
        if (items.Count == 0)
        {
            return 0;
        }

        var urls = items.Select(x => Truncate(x.Url, 1024)).ToArray();
        var cities = items.Select(x => TruncateNullable(x.City, 128)).ToArray();
        var idempotencyKeys = items.Select(x => Truncate(x.IdempotencyKey, 128)).ToArray();

        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            $@"insert into price_collect_queue(run_id, url, city, status, attempt, max_attempts, next_attempt_at, idempotency_key)
select @run_id, x.url, x.city, @status, 0, @max_attempts, now(), x.idempotency_key
from unnest(@urls::varchar[], @cities::varchar[], @idempotency_keys::varchar[]) as x(url, city, idempotency_key)
on conflict (run_id, url) do nothing;";
        AddParam(cmd, "@run_id", runId);
        AddParam(cmd, "@status", QueueItemStatuses.Pending);
        AddParam(cmd, "@max_attempts", Math.Max(1, maxAttempts));
        AddParam(cmd, "@urls", urls);
        AddParam(cmd, "@cities", cities);
        AddParam(cmd, "@idempotency_keys", idempotencyKeys);

        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ReservedQueueItem>> ReserveBatchAsync(
        long runId,
        int batchSize,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken ct)
    {
        var safeBatch = Math.Max(1, batchSize);
        var safeLeaseSeconds = Math.Max(1, (int)Math.Ceiling(leaseDuration.TotalSeconds));

        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = $"""
                           with candidates as (
                               select queue_id
                               from price_collect_queue
                               where run_id=@run_id
                                 and status in (@pending, @retry)
                                 and next_attempt_at <= now()
                               order by next_attempt_at, queue_id
                               limit @limit
                               for update skip locked
                           ),
                           updated as (
                               update price_collect_queue q
                               set status=@reserved,
                                   reserved_at=now(),
                                   lease_until=now() + (@lease_seconds * interval '1 second'),
                                   reserved_by=@reserved_by,
                                   updated_at=now()
                               from candidates c
                               where q.queue_id = c.queue_id
                               returning q.queue_id, q.url, q.city, q.attempt, q.max_attempts, q.idempotency_key
                           )
                           select queue_id, url, city, attempt, max_attempts, idempotency_key
                           from updated
                           order by queue_id;
                           """;
        AddParam(cmd, "@run_id", runId);
        AddParam(cmd, "@pending", QueueItemStatuses.Pending);
        AddParam(cmd, "@retry", QueueItemStatuses.Retry);
        AddParam(cmd, "@reserved", QueueItemStatuses.Reserved);
        AddParam(cmd, "@reserved_by", Truncate(workerId, 128));
        AddParam(cmd, "@lease_seconds", safeLeaseSeconds);
        AddParam(cmd, "@limit", safeBatch);

        var result = new List<ReservedQueueItem>(safeBatch);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new ReservedQueueItem(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetString(5)));
        }

        return result;
    }

    public async Task MarkSucceededAsync(long queueId, CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = """
                          update price_collect_queue
                          set status=@status,
                              finished_at=now(),
                              reserved_at=null,
                              lease_until=null,
                              reserved_by=null,
                              updated_at=now()
                          where queue_id=@queue_id;
                          """;
        AddParam(cmd, "@status", QueueItemStatuses.Succeeded);
        AddParam(cmd, "@queue_id", queueId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkRetryAsync(long queueId, string errorCode, int? httpStatus, string? message,
        DateTimeOffset nextAttemptAt, CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = """
                          update price_collect_queue
                          set status=@status,
                              attempt=attempt+1,
                              next_attempt_at=@next_attempt_at,
                              last_error_code=@error_code,
                              last_http_status=@http_status,
                              last_error_message=@error_message,
                              reserved_at=null,
                              lease_until=null,
                              reserved_by=null,
                              updated_at=now()
                          where queue_id=@queue_id;
                          """;
        AddParam(cmd, "@status", QueueItemStatuses.Retry);
        AddParam(cmd, "@next_attempt_at", nextAttemptAt.UtcDateTime);
        AddParam(cmd, "@error_code", Truncate(errorCode, 64));
        AddParam(cmd, "@http_status", httpStatus);
        AddParam(cmd, "@error_message", Truncate(message, 512));
        AddParam(cmd, "@queue_id", queueId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkDeadAsync(long queueId, string errorCode, int? httpStatus, string? message,
        CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = """
                          update price_collect_queue
                          set status=@status,
                              attempt=attempt+1,
                              last_error_code=@error_code,
                              last_http_status=@http_status,
                              last_error_message=@error_message,
                              reserved_at=null,
                              lease_until=null,
                              reserved_by=null,
                              updated_at=now(),
                              finished_at=now()
                          where queue_id=@queue_id;
                          """;
        AddParam(cmd, "@status", QueueItemStatuses.Dead);
        AddParam(cmd, "@error_code", Truncate(errorCode, 64));
        AddParam(cmd, "@http_status", httpStatus);
        AddParam(cmd, "@error_message", Truncate(message, 512));
        AddParam(cmd, "@queue_id", queueId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> ReapExpiredReservationsAsync(long runId, CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = """
                          update price_collect_queue
                          set status=@retry_status,
                              next_attempt_at=now(),
                              reserved_at=null,
                              lease_until=null,
                              reserved_by=null,
                              updated_at=now(),
                              last_error_code=coalesce(last_error_code, @lease_code),
                              last_error_message=coalesce(last_error_message, @lease_message)
                          where run_id=@run_id
                            and status=@reserved_status
                            and lease_until is not null
                            and lease_until < now();
                          """;
        AddParam(cmd, "@retry_status", QueueItemStatuses.Retry);
        AddParam(cmd, "@reserved_status", QueueItemStatuses.Reserved);
        AddParam(cmd, "@lease_code", "lease_expired");
        AddParam(cmd, "@lease_message", "Reservation lease expired");
        AddParam(cmd, "@run_id", runId);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> HasOutstandingItemsAsync(long runId, CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = """
                          select exists (
                              select 1
                              from price_collect_queue
                              where run_id=@run_id
                                and status in (@pending, @retry, @reserved)
                          );
                          """;
        AddParam(cmd, "@run_id", runId);
        AddParam(cmd, "@pending", QueueItemStatuses.Pending);
        AddParam(cmd, "@retry", QueueItemStatuses.Retry);
        AddParam(cmd, "@reserved", QueueItemStatuses.Reserved);
        var scalar = await cmd.ExecuteScalarAsync(ct);
        return scalar is true || (scalar is not null && Convert.ToBoolean(scalar));
    }

    public async Task<QueueRunStats> GetRunStatsAsync(long runId, CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = """
                          select
                              count(*) filter (where status=@pending) as pending_count,
                              count(*) filter (where status=@reserved) as reserved_count,
                              count(*) filter (where status=@retry) as retry_count,
                              count(*) filter (where status=@succeeded) as succeeded_count,
                              count(*) filter (where status=@dead) as dead_count
                          from price_collect_queue
                          where run_id=@run_id;
                          """;
        AddParam(cmd, "@pending", QueueItemStatuses.Pending);
        AddParam(cmd, "@reserved", QueueItemStatuses.Reserved);
        AddParam(cmd, "@retry", QueueItemStatuses.Retry);
        AddParam(cmd, "@succeeded", QueueItemStatuses.Succeeded);
        AddParam(cmd, "@dead", QueueItemStatuses.Dead);
        AddParam(cmd, "@run_id", runId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return new QueueRunStats(0, 0, 0, 0, 0);
        }

        return new QueueRunStats(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4));
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

    private static string? TruncateNullable(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
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
