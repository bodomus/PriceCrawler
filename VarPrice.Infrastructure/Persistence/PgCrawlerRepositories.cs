using VarPrice.Domain.Enums;
using VarPrice.Domain.Interfaces;
using VarPrice.Domain.Models;
using VarPrice.Domain.ValueObjects;

namespace VarPrice.Infrastructure.Persistence;

public sealed class PgCrawlerRunRepository(PgRoutineExecutor routineExecutor) : ICrawlerRunRepository
{
    public async Task<long> StartAsync(string source, CancellationToken ct)
        => await routineExecutor.ExecuteScalarAsync<long?>(
               DbRoutineCall.ScalarFunction("crawler_run_start")
                   .AddParameter("p_source", source),
               ct)
           ?? throw new InvalidOperationException("DB routine 'crawler_run_start' did not return a run id.");

    public async Task FinishAsync(long runId, RunStatus status, string? note, CancellationToken ct)
        => await routineExecutor.ExecuteAsync(
            DbRoutineCall.Procedure("crawler_run_finish")
                .AddParameter("p_run_id", runId)
                .AddParameter("p_status", ToStorage(status))
                .AddParameter("p_note", note),
            ct);

    private static string ToStorage(RunStatus status)
        => status switch
        {
            RunStatus.Running => "running",
            RunStatus.Ok => "ok",
            _ => "error"
        };
}

public sealed class PgIngestionRunRepository(PgRoutineExecutor routineExecutor) : IIngestionRunRepository
{
    public async Task<long> StartAsync(long crawlerRunId, CancellationToken ct)
        => await routineExecutor.ExecuteScalarAsync<long?>(
               DbRoutineCall.ScalarFunction("ingestion_run_start")
                   .AddParameter("p_crawler_run_id", crawlerRunId),
               ct)
           ?? throw new InvalidOperationException(
               "DB routine 'ingestion_run_start' did not return an ingestion run id.");

    public async Task FinishAsync(long ingestionRunId, RunStatus status, ErrorInfo? errorInfo, CancellationToken ct)
        => await routineExecutor.ExecuteAsync(
            DbRoutineCall.Procedure("ingestion_run_finish")
                .AddParameter("p_ingestion_run_id", ingestionRunId)
                .AddParameter("p_status", status == RunStatus.Ok ? "ok" : "error")
                .AddParameter("p_error_code", errorInfo?.Code)
                .AddParameter("p_error_message", errorInfo?.Message),
            ct);
}

public sealed class PgPriceSnapshotRepository(PgRoutineExecutor routineExecutor)
    : IPriceSnapshotRepository
{
    public async Task<ProductObservationWriteResult> StoreObservationAsync(
        long runId,
        long? queueId,
        ProductObservation observation,
        CancellationToken ct)
    {
        var result = await routineExecutor.QuerySingleOrDefaultAsync(
            DbRoutineCall.SetReturningFunction("price_observation_store")
                .AddParameter("p_run_id", runId)
                .AddParameter("p_queue_id", queueId)
                .AddParameter("p_external_id", observation.ExternalId)
                .AddParameter("p_name", observation.Name)
                .AddParameter("p_url", observation.Url)
                .AddParameter("p_slug", observation.Slug)
                .AddParameter("p_pack_value", observation.PackValue)
                .AddParameter("p_pack_unit", observation.PackUnit)
                .AddParameter("p_price", observation.Price)
                .AddParameter("p_old_price", observation.OldPrice)
                .AddParameter("p_promo_flag", observation.PromoFlag)
                .AddParameter("p_in_stock", observation.InStock)
                .AddParameter("p_observed_at", observation.ObservedAtUtc.UtcDateTime),
            reader => new ProductObservationWriteResult(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetInt64(1),
                reader.GetBoolean(2)),
            ct);

        return result ?? throw new InvalidOperationException(
            "DB routine 'price_observation_store' did not return a write result.");
    }

    public async Task<long> InsertCrawlErrorAsync(CrawlErrorRecord error, CancellationToken ct)
        => await routineExecutor.ExecuteScalarAsync<long?>(
               DbRoutineCall.ScalarFunction("crawl_error_add")
                   .AddParameter("p_run_id", error.RunId)
                   .AddParameter("p_queue_id", error.QueueId)
                   .AddParameter("p_product_id", error.ProductId)
                   .AddParameter("p_url", error.Url)
                   .AddParameter("p_created_at", error.CreatedAtUtc.UtcDateTime)
                   .AddParameter("p_error_code", error.ErrorCode)
                   .AddParameter("p_http_status", error.HttpStatus)
                   .AddParameter("p_error_message", error.ErrorMessage),
               ct)
           ?? throw new InvalidOperationException("DB routine 'crawl_error_add' did not return an error id.");
}

public sealed class PgPriceCollectQueueRepository(PgRoutineExecutor routineExecutor) : IPriceCollectQueueRepository
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
        return await routineExecutor.ExecuteScalarAsync<int?>(
                   DbRoutineCall.ScalarFunction("price_collect_queue_enqueue")
                       .AddParameter("p_run_id", runId)
                       .AddParameter("p_urls", urls)
                       .AddParameter("p_idempotency_keys", idempotencyKeys)
                       .AddParameter("p_max_attempts", Math.Max(1, maxAttempts)),
                   ct)
               ?? 0;
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

        return await routineExecutor.QueryAsync(
            DbRoutineCall.SetReturningFunction("price_collect_queue_reserve_batch")
                .AddParameter("p_run_id", runId)
                .AddParameter("p_batch_size", safeBatch)
                .AddParameter("p_worker_id", workerId)
                .AddParameter("p_lease_seconds", safeLeaseSeconds),
            reader => new ReservedQueueItem(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4)),
            ct);
    }

    public async Task MarkSucceededAsync(long queueId, CancellationToken ct)
        => await routineExecutor.ExecuteAsync(
            DbRoutineCall.Procedure("price_collect_queue_mark_succeeded")
                .AddParameter("p_queue_id", queueId),
            ct);

    public async Task MarkRetryAsync(long queueId, string errorCode, int? httpStatus, string? message,
        DateTimeOffset nextAttemptAt, CancellationToken ct)
        => await routineExecutor.ExecuteAsync(
            DbRoutineCall.Procedure("price_collect_queue_mark_retry")
                .AddParameter("p_queue_id", queueId)
                .AddParameter("p_error_code", errorCode)
                .AddParameter("p_http_status", httpStatus)
                .AddParameter("p_error_message", message)
                .AddParameter("p_next_attempt_at", nextAttemptAt.UtcDateTime),
            ct);

    public async Task MarkDeadAsync(long queueId, string errorCode, int? httpStatus, string? message,
        CancellationToken ct)
        => await routineExecutor.ExecuteAsync(
            DbRoutineCall.Procedure("price_collect_queue_mark_dead")
                .AddParameter("p_queue_id", queueId)
                .AddParameter("p_error_code", errorCode)
                .AddParameter("p_http_status", httpStatus)
                .AddParameter("p_error_message", message),
            ct);

    public async Task<int> ReapExpiredReservationsAsync(long runId, CancellationToken ct)
        => await routineExecutor.ExecuteScalarAsync<int?>(
               DbRoutineCall.ScalarFunction("price_collect_queue_reap_expired")
                   .AddParameter("p_run_id", runId),
               ct)
           ?? 0;

    public async Task<bool> HasOutstandingItemsAsync(long runId, CancellationToken ct)
        => await routineExecutor.ExecuteScalarAsync<bool?>(
               DbRoutineCall.ScalarFunction("price_collect_queue_has_outstanding")
                   .AddParameter("p_run_id", runId),
               ct)
           ?? false;

    public async Task<QueueRunStats> GetRunStatsAsync(long runId, CancellationToken ct)
    {
        var result = await routineExecutor.QuerySingleOrDefaultAsync(
            DbRoutineCall.SetReturningFunction("price_collect_queue_get_run_stats")
                .AddParameter("p_run_id", runId),
            reader => new QueueRunStats(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4)),
            ct);

        return result ?? new QueueRunStats(0, 0, 0, 0, 0);
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
}
