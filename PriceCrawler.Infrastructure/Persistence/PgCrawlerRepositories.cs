using System.Data.Common;
using System.Text.Json;

using PriceCrawler.Domain.Enums;
using PriceCrawler.Domain.Interfaces;
using PriceCrawler.Domain.Models;
using PriceCrawler.Domain.ValueObjects;

namespace PriceCrawler.Infrastructure.Persistence;

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

    public async Task<long> StartAsync(string runType, string source, string? discoverySource, CancellationToken ct)
        => await routineExecutor.ExecuteScalarAsync<long?>(
               DbRoutineCall.ScalarFunction("crawler_run_start")
                   .AddParameter("p_run_type", runType)
                   .AddParameter("p_source", source)
                   .AddParameter("p_discovery_source", discoverySource), ct)
           ?? throw new InvalidOperationException("DB routine 'crawler_run_start' did not return a run id.");

    public async Task CompleteAsync(long runId, RunStatus status, CrawlerRunStatistics statistics,
        IReadOnlyCollection<CrawlerRunStageTiming> stageTimings, string? note, string? errorCode,
        string? errorMessage, CancellationToken ct)
    {
        var stagesJson = JsonSerializer.Serialize(stageTimings.Select(x => new
        {
            stage = x.Stage,
            duration_ms = x.DurationMs,
            item_count = x.ItemCount
        }));
        await routineExecutor.ExecuteAsync(
            DbRoutineCall.Procedure("crawler_run_complete")
                .AddParameter("p_run_id", runId).AddParameter("p_status", ToStorage(status))
                .AddParameter("p_discovered_count", statistics.DiscoveredCount)
                .AddParameter("p_accepted_count", statistics.AcceptedCount)
                .AddParameter("p_inserted_count", statistics.InsertedCount)
                .AddParameter("p_updated_count", statistics.UpdatedCount)
                .AddParameter("p_reactivated_count", statistics.ReactivatedCount)
                .AddParameter("p_deactivated_count", statistics.DeactivatedCount)
                .AddParameter("p_selected_count", statistics.SelectedCount)
                .AddParameter("p_enqueued_count", statistics.EnqueuedCount)
                .AddParameter("p_succeeded_count", statistics.SucceededCount)
                .AddParameter("p_retry_count", statistics.RetryCount)
                .AddParameter("p_dead_count", statistics.DeadCount)
                .AddParameter("p_failed_count", statistics.FailedCount)
                .AddParameter("p_products_created_count", statistics.ProductsCreatedCount)
                .AddParameter("p_products_updated_count", statistics.ProductsUpdatedCount)
                .AddParameter("p_snapshots_created_count", statistics.SnapshotsCreatedCount)
                .AddParameter("p_errors_created_count", statistics.ErrorsCreatedCount)
                .AddParameter("p_stages_json", stagesJson).AddParameter("p_note", note)
                .AddParameter("p_error_code", errorCode).AddParameter("p_error_message", errorMessage), ct);
    }

    private static string ToStorage(RunStatus status)
        => status switch
        {
            RunStatus.Running => "running",
            RunStatus.Ok => "ok",
            _ => "error"
        };
}

public sealed class PgCrawlerRunReadRepository(PgRoutineExecutor routineExecutor) : ICrawlerRunReadRepository
{
    public async Task<CrawlerRunDetails?> GetByIdAsync(long runId, CancellationToken ct)
    {
        var run = await routineExecutor.QuerySingleOrDefaultAsync(
            DbRoutineCall.SetReturningFunction("crawler_run_get_by_id").AddParameter("p_run_id", runId),
            MapDetailsWithoutStages, ct);
        if (run is null) return null;
        var stages = await routineExecutor.QueryAsync(
            DbRoutineCall.SetReturningFunction("crawler_run_stage_get").AddParameter("p_run_id", runId),
            r => new CrawlerRunStageTiming(r.GetString(0), r.GetInt64(1), r.IsDBNull(2) ? null : r.GetInt32(2)), ct);
        return run with { StageTimings = stages };
    }

    public Task<IReadOnlyList<CrawlerRunSummary>> GetRecentAsync(int limit, string? runType, string? status,
        CancellationToken ct) => routineExecutor.QueryAsync(
        DbRoutineCall.SetReturningFunction("crawler_run_get_recent")
            .AddParameter("p_limit", Math.Clamp(limit, 1, 200)).AddParameter("p_run_type", runType)
            .AddParameter("p_status", status),
        r => new CrawlerRunSummary(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), ToOffset(r, 4),
            r.IsDBNull(5) ? null : ToOffset(r, 5), r.IsDBNull(6) ? null : r.GetInt64(6), r.GetInt32(7),
            r.GetInt32(8), r.GetInt32(9), r.IsDBNull(10) ? null : r.GetString(10)), ct);

    public async Task<CrawlerRunAggregateStatistics> GetAggregateAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc,
        string? runType, CancellationToken ct)
    {
        var row = await routineExecutor.QuerySingleOrDefaultAsync(
            DbRoutineCall.SetReturningFunction("crawler_run_get_aggregate")
                .AddParameter("p_from", fromUtc.UtcDateTime).AddParameter("p_to", toUtc.UtcDateTime)
                .AddParameter("p_run_type", runType),
            r => new CrawlerRunAggregateStatistics(fromUtc, toUtc, runType, r.GetInt32(0), r.GetInt32(1),
                r.GetInt32(2), r.GetInt64(3), r.GetDouble(4), r.GetInt64(5), r.GetInt64(6), r.GetInt64(7),
                r.GetInt64(8), r.GetInt64(9), r.GetInt64(10), r.GetInt64(11)), ct);
        return row ?? new CrawlerRunAggregateStatistics(fromUtc, toUtc, runType, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    private static CrawlerRunDetails MapDetailsWithoutStages(DbDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4),
        ToOffset(r, 5), r.IsDBNull(6) ? null : ToOffset(r, 6), r.IsDBNull(7) ? null : r.GetInt64(7),
        new CrawlerRunStatistics(r.GetInt32(8), r.GetInt32(9), r.GetInt32(10), r.GetInt32(11),
            r.GetInt32(12), r.GetInt32(13), r.GetInt32(14), r.GetInt32(15), r.GetInt32(16), r.GetInt32(17),
            r.GetInt32(18), r.GetInt32(19), r.GetInt32(20), r.GetInt32(21), r.GetInt32(22), r.GetInt32(23)),
        [], r.IsDBNull(24) ? null : r.GetString(24), r.IsDBNull(25) ? null : r.GetString(25),
        r.IsDBNull(26) ? null : r.GetString(26));

    private static DateTimeOffset ToOffset(DbDataReader reader, int ordinal)
        => new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
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
                reader.GetBoolean(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4)),
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
    public async Task<QueueEnqueueResult> EnqueueAsync(long runId, IReadOnlyCollection<QueueEnqueueItem> items, int maxAttempts,
        CancellationToken ct)
    {
        if (items.Count == 0)
        {
            return new QueueEnqueueResult(0, 0, 0, []);
        }

        var urls = items.Select(x => Truncate(x.Url, 1024)).ToArray();
        var idempotencyKeys = items.Select(x => Truncate(x.IdempotencyKey, 128)).ToArray();
        var productCatalogIds = items.Select(x => x.ProductCatalogId).ToArray();
        var pageKinds = items.Select(x => ToStorage(x.PageKind)).ToArray();
        return await routineExecutor.QuerySingleOrDefaultAsync(
                   DbRoutineCall.SetReturningFunction("price_collect_queue_enqueue_result")
                       .AddParameter("p_run_id", runId)
                       .AddParameter("p_urls", urls)
                       .AddParameter("p_idempotency_keys", idempotencyKeys)
                       .AddParameter("p_max_attempts", Math.Max(1, maxAttempts))
                       .AddParameter("p_product_catalog_ids", productCatalogIds)
                       .AddParameter("p_page_kinds", pageKinds),
                   reader => new QueueEnqueueResult(
                       reader.GetInt32(0),
                       reader.GetInt32(1),
                       reader.GetInt32(2),
                       reader.IsDBNull(3) ? [] : reader.GetFieldValue<long[]>(3)),
                   ct)
               ?? new QueueEnqueueResult(0, 0, 0, []);
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
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                FromStorage(reader.IsDBNull(6) ? null : reader.GetString(6))),
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

    private static string ToStorage(QueueItemKind kind) =>
        kind switch
        {
            QueueItemKind.ListingPage => "listing_page",
            QueueItemKind.CategoryPage => "category_page",
            QueueItemKind.SitemapPage => "sitemap_page",
            QueueItemKind.ApiPage => "api_page",
            QueueItemKind.Unknown => "unknown",
            _ => "product_page"
        };

    private static QueueItemKind FromStorage(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "listing_page" => QueueItemKind.ListingPage,
            "category_page" => QueueItemKind.CategoryPage,
            "sitemap_page" => QueueItemKind.SitemapPage,
            "api_page" => QueueItemKind.ApiPage,
            "unknown" => QueueItemKind.Unknown,
            _ => QueueItemKind.ProductPage
        };
}
