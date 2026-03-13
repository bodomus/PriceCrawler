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
        AddParam(cmd, "@source", Truncate(source, 64));
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
        AddParam(cmd, "@note", TruncateNullable(note, 255));
        AddParam(cmd, "@run_id", runId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static short ToStorage(RunStatus status) => (short)status;

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
        AddParam(cmd, "@status", status == RunStatus.Ok ? "ok" : "error");
        AddParam(cmd, "@error_code", TruncateNullable(errorInfo?.Code, 128));
        AddParam(cmd, "@error_message", TruncateNullable(errorInfo?.Message, 512));
        AddParam(cmd, "@ingestion_run_id", ingestionRunId);
        await cmd.ExecuteNonQueryAsync(ct);
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

public sealed class PgPriceSnapshotRepository(IPgConnectionFactory factory) : IPriceSnapshotRepository
{
    public async Task<ProductObservationWriteResult> StoreObservationAsync(
        long runId,
        long? queueId,
        ProductObservation observation,
        CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);

        var productKey = await UpsertProductAsync(cn, tx, observation, ct);
        var latestSnapshot = await GetLatestSnapshotAsync(cn, tx, productKey, ct);
        var snapshotId = latestSnapshot?.SnapshotId;
        var snapshotCreated = false;

        if (observation.HasMinimalValidState)
        {
            if (latestSnapshot is null || HasMeaningfulChange(latestSnapshot, observation))
            {
                snapshotId = await InsertSnapshotAsync(cn, tx, runId, productKey, queueId, observation, ct);
                snapshotCreated = true;
            }
        }

        await tx.CommitAsync(ct);
        return new ProductObservationWriteResult(productKey, snapshotId, snapshotCreated);
    }

    public async Task<long> InsertProductErrorAsync(ProductErrorRecord error, CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = @"insert into product_errors(
product_key,
run_id,
price_snapshot_id,
queue_id,
occurred_at,
stage,
error_code,
error_message,
details_json)
values(
@product_key,
@run_id,
@price_snapshot_id,
@queue_id,
@occurred_at,
@stage,
@error_code,
@error_message,
cast(@details_json as jsonb))
returning product_error_id;";
        AddParam(cmd, "@product_key", error.ProductKey);
        AddParam(cmd, "@run_id", error.RunId);
        AddParam(cmd, "@price_snapshot_id", error.PriceSnapshotId);
        AddParam(cmd, "@queue_id", error.QueueId);
        AddParam(cmd, "@occurred_at", error.OccurredAtUtc.UtcDateTime);
        AddParam(cmd, "@stage", Truncate(error.Stage, 64));
        AddParam(cmd, "@error_code", Truncate(NormalizeErrorCode(error.ErrorCode), 64));
        AddParam(cmd, "@error_message", Truncate(error.ErrorMessage, 512));
        AddParam(cmd, "@details_json", string.IsNullOrWhiteSpace(error.DetailsJson) ? null : error.DetailsJson);
        var scalar = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(scalar);
    }

    private static async Task<long> UpsertProductAsync(
        DbConnection cn,
        DbTransaction tx,
        ProductObservation observation,
        CancellationToken ct)
    {
        await using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"insert into product(product_id, name, url, pack_value, pack_unit, last_seen_at)
values(@pid, @name, @url, @pv, @pu, @last_seen_at)
on conflict (product_id) do update set
name=excluded.name,
url=excluded.url,
pack_value=excluded.pack_value,
pack_unit=excluded.pack_unit,
last_seen_at=excluded.last_seen_at
returning product_key;";
        AddParam(cmd, "@pid", Truncate(observation.ProductId, 64));
        AddParam(cmd, "@name", Truncate(observation.Name, 512));
        AddParam(cmd, "@url", Truncate(observation.Url, 1024));
        AddParam(cmd, "@pv", observation.PackValue);
        AddParam(cmd, "@pu", TruncateNullable(observation.PackUnit, 16));
        AddParam(cmd, "@last_seen_at", observation.ObservedAtUtc.UtcDateTime);
        var scalar = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(scalar);
    }

    private static async Task<SnapshotState?> GetLatestSnapshotAsync(
        DbConnection cn,
        DbTransaction tx,
        long productKey,
        CancellationToken ct)
    {
        await using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"select snapshot_id, regular_price, final_price, discount_percent, promo_flag, in_stock
from price_snapshot
where product_key=@product_key
order by captured_at desc, snapshot_id desc
limit 1;";
        AddParam(cmd, "@product_key", productKey);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new SnapshotState(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetDecimal(1),
            reader.IsDBNull(2) ? null : reader.GetDecimal(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            reader.GetBoolean(4),
            reader.IsDBNull(5) ? null : reader.GetBoolean(5));
    }

    private static async Task<long> InsertSnapshotAsync(
        DbConnection cn,
        DbTransaction tx,
        long runId,
        long productKey,
        long? queueId,
        ProductObservation observation,
        CancellationToken ct)
    {
        await using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"insert into price_snapshot(
run_id,
captured_at,
product_key,
city,
regular_price,
final_price,
discount_percent,
promo_flag,
in_stock,
queue_id)
values(
@run_id,
@captured_at,
@product_key,
@city,
@regular_price,
@final_price,
@discount_percent,
@promo_flag,
@in_stock,
@queue_id)
returning snapshot_id;";
        AddParam(cmd, "@run_id", runId);
        AddParam(cmd, "@captured_at", observation.ObservedAtUtc.UtcDateTime);
        AddParam(cmd, "@product_key", productKey);
        AddParam(cmd, "@queue_id", queueId);
        AddParam(cmd, "@city", TruncateNullable(observation.City, 128));
        AddParam(cmd, "@regular_price", observation.RegularPrice);
        AddParam(cmd, "@final_price", observation.FinalPrice);
        AddParam(cmd, "@discount_percent", observation.DiscountPercent);
        AddParam(cmd, "@promo_flag", observation.PromoFlag);
        AddParam(cmd, "@in_stock", observation.InStock);
        var scalar = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(scalar);
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

    private static string NormalizeErrorCode(string? errorCode)
        => string.IsNullOrWhiteSpace(errorCode)
            ? CrawlerErrorCodes.Unknown
            : errorCode.Trim().ToLowerInvariant();

    private static bool HasMeaningfulChange(SnapshotState snapshot, ProductObservation observation)
        => snapshot.RegularPrice != observation.RegularPrice
           || snapshot.FinalPrice != observation.FinalPrice
           || snapshot.DiscountPercent != observation.DiscountPercent
           || snapshot.PromoFlag != observation.PromoFlag
           || snapshot.InStock != observation.InStock;

    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    private sealed record SnapshotState(
        long SnapshotId,
        decimal? RegularPrice,
        decimal? FinalPrice,
        int? DiscountPercent,
        bool PromoFlag,
        bool? InStock);
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
