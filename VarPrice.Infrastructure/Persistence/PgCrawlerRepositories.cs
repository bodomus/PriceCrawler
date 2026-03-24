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
        cmd.CommandText = "insert into crawler_run(status, source) values (@status, @source) returning id;";
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
        cmd.CommandText = "update crawler_run set status=@status, note=@note, finished_at=now() where id=@id;";
        AddParam(cmd, "@status", ToStorage(status));
        AddParam(cmd, "@note", TruncateNullable(note, 255));
        AddParam(cmd, "@id", runId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string ToStorage(RunStatus status)
        => status switch
        {
            RunStatus.Running => "running",
            RunStatus.Ok => "ok",
            _ => "error"
        };

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

        var productId = await UpsertProductAsync(cn, tx, observation, ct);
        var latestSnapshot = await GetLatestSnapshotAsync(cn, tx, productId, ct);
        var snapshotId = latestSnapshot?.Id;
        var snapshotCreated = false;

        if (observation.HasMinimalValidState)
        {
            if (latestSnapshot is null || HasMeaningfulChange(latestSnapshot, observation))
            {
                snapshotId = await InsertSnapshotAsync(cn, tx, runId, productId, queueId, observation, ct);
                snapshotCreated = true;
            }
        }

        await tx.CommitAsync(ct);
        return new ProductObservationWriteResult(productId, snapshotId, snapshotCreated);
    }

    public async Task<long> InsertCrawlErrorAsync(CrawlErrorRecord error, CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = @"insert into crawl_error(
 run_id,
 queue_id,
 product_id,
 url,
 error_code,
 http_status,
 error_message,
 created_at)
 values(
 @run_id,
 @queue_id,
 @product_id,
 @url,
 @error_code,
 @http_status,
 @error_message,
 @created_at)
 returning id;";
        AddParam(cmd, "@run_id", error.RunId);
        AddParam(cmd, "@queue_id", error.QueueId);
        AddParam(cmd, "@product_id", error.ProductId);
        AddParam(cmd, "@url", TruncateNullable(error.Url, 1024));
        AddParam(cmd, "@error_code", TruncateNullable(NormalizeErrorCode(error.ErrorCode), 64));
        AddParam(cmd, "@http_status", error.HttpStatus);
        AddParam(cmd, "@error_message", TruncateNullable(error.ErrorMessage, 512));
        AddParam(cmd, "@created_at", error.CreatedAtUtc.UtcDateTime);
        var scalar = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(scalar);
    }

    private static async Task<long> UpsertProductAsync(
        DbConnection cn,
        DbTransaction tx,
        ProductObservation observation,
        CancellationToken ct)
    {
        var existingProductId = await FindExistingProductIdAsync(cn, tx, observation.Url, observation.ExternalId, ct);

        await using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;

        if (existingProductId.HasValue)
        {
            cmd.CommandText = @"update product
set external_id = coalesce(@external_id, external_id),
    name = @name,
    url = @url,
    slug = @slug,
    pack_value = @pack_value,
    pack_unit = @pack_unit,
    updated_at = @updated_at
where id = @id
returning id;";
            AddParam(cmd, "@id", existingProductId.Value);
        }
        else
        {
            cmd.CommandText = @"insert into product(
external_id,
name,
url,
slug,
pack_value,
pack_unit,
created_at,
updated_at)
values(
@external_id,
@name,
@url,
@slug,
@pack_value,
@pack_unit,
@created_at,
@updated_at)
returning id;";
            AddParam(cmd, "@created_at", observation.ObservedAtUtc.UtcDateTime);
        }

        AddParam(cmd, "@external_id", TruncateNullable(observation.ExternalId, 64));
        AddParam(cmd, "@name", Truncate(observation.Name, 512));
        AddParam(cmd, "@url", Truncate(observation.Url, 1024));
        AddParam(cmd, "@slug", TruncateNullable(observation.Slug, 512));
        AddParam(cmd, "@pack_value", observation.PackValue);
        AddParam(cmd, "@pack_unit", TruncateNullable(observation.PackUnit, 16));
        AddParam(cmd, "@updated_at", observation.ObservedAtUtc.UtcDateTime);
        var scalar = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(scalar);
    }

    private static async Task<long?> FindExistingProductIdAsync(
        DbConnection cn,
        DbTransaction tx,
        string url,
        string? externalId,
        CancellationToken ct)
    {
        await using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
                          select id
                          from product
                          where url = @url
                             or (@external_id is not null and external_id = @external_id)
                          order by case when url = @url then 0 else 1 end, id
                          limit 1;
                          """;
        AddParam(cmd, "@url", Truncate(url, 1024));
        AddParam(cmd, "@external_id", TruncateNullable(externalId, 64));
        var scalar = await cmd.ExecuteScalarAsync(ct);
        return scalar is null or DBNull ? null : Convert.ToInt64(scalar);
    }

    private static async Task<SnapshotState?> GetLatestSnapshotAsync(
        DbConnection cn,
        DbTransaction tx,
        long productId,
        CancellationToken ct)
    {
        await using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"select id, price, old_price, promo_flag, in_stock
from price_snapshot
where product_id=@product_id
order by captured_at desc, id desc
limit 1;";
        AddParam(cmd, "@product_id", productId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new SnapshotState(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetDecimal(1),
            reader.IsDBNull(2) ? null : reader.GetDecimal(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4));
    }

    private static async Task<long> InsertSnapshotAsync(
        DbConnection cn,
        DbTransaction tx,
        long runId,
        long productId,
        long? queueId,
        ProductObservation observation,
        CancellationToken ct)
    {
        await using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"insert into price_snapshot(
run_id,
product_id,
captured_at,
price,
old_price,
promo_flag,
in_stock,
queue_id)
values(
@run_id,
@product_id,
@captured_at,
@price,
@old_price,
@promo_flag,
@in_stock,
@queue_id)
returning id;";
        AddParam(cmd, "@run_id", runId);
        AddParam(cmd, "@product_id", productId);
        AddParam(cmd, "@captured_at", observation.ObservedAtUtc.UtcDateTime);
        AddParam(cmd, "@price", observation.Price);
        AddParam(cmd, "@old_price", observation.OldPrice);
        AddParam(cmd, "@promo_flag", observation.PromoFlag);
        AddParam(cmd, "@in_stock", observation.InStock);
        AddParam(cmd, "@queue_id", queueId);
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
        => snapshot.Price != observation.Price
           || snapshot.OldPrice != observation.OldPrice
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
        long Id,
        decimal? Price,
        decimal? OldPrice,
        bool PromoFlag,
        bool InStock);
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
        var idempotencyKeys = items.Select(x => Truncate(x.IdempotencyKey, 128)).ToArray();

        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            $@"insert into price_collect_queue(run_id, url, status, attempt, max_attempts, next_attempt_at, idempotency_key, created_at, updated_at)
 select @run_id, x.url, @status, 0, @max_attempts, now(), x.idempotency_key, now(), now()
 from unnest(@urls::varchar[], @idempotency_keys::varchar[]) as x(url, idempotency_key)
 on conflict (run_id, url) do nothing;";
        AddParam(cmd, "@run_id", runId);
        AddParam(cmd, "@status", QueueItemStatuses.Pending);
        AddParam(cmd, "@max_attempts", Math.Max(1, maxAttempts));
        AddParam(cmd, "@urls", urls);
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
                               select id
                               from price_collect_queue
                               where run_id=@run_id
                                 and status in (@pending, @retry)
                                 and coalesce(next_attempt_at, created_at, now()) <= now()
                               order by coalesce(next_attempt_at, created_at, now()), id
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
                               where q.id = c.id
                               returning q.id, q.url, q.attempt, q.max_attempts, q.idempotency_key
                           )
                           select id, url, attempt, max_attempts, coalesce(idempotency_key, '')
                           from updated
                           order by id;
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
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4)));
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
                          where id=@id;
                          """;
        AddParam(cmd, "@status", QueueItemStatuses.Succeeded);
        AddParam(cmd, "@id", queueId);
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
                          where id=@id;
                          """;
        AddParam(cmd, "@status", QueueItemStatuses.Retry);
        AddParam(cmd, "@next_attempt_at", nextAttemptAt.UtcDateTime);
        AddParam(cmd, "@error_code", Truncate(errorCode, 64));
        AddParam(cmd, "@http_status", httpStatus);
        AddParam(cmd, "@error_message", TruncateNullable(message, 512));
        AddParam(cmd, "@id", queueId);
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
                          where id=@id;
                          """;
        AddParam(cmd, "@status", QueueItemStatuses.Dead);
        AddParam(cmd, "@error_code", Truncate(errorCode, 64));
        AddParam(cmd, "@http_status", httpStatus);
        AddParam(cmd, "@error_message", TruncateNullable(message, 512));
        AddParam(cmd, "@id", queueId);
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
