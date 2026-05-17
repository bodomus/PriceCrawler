using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Npgsql;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;
using VarPrice.Application.UseCases;
using VarPrice.Domain.Enums;
using VarPrice.Domain.Models;
using VarPrice.Domain.ValueObjects;
using VarPrice.Infrastructure.Persistence;

namespace VarPrice.Web.Tests;

public sealed class WorkerIntegrationTests
{
    private const string ConnectionString =
        "Host=localhost;Port=55432;Database=varprice;Username=var;Password=myPassword";

    [Fact]
    public async Task RunCrawlerUseCase_PersistsRunAndSnapshots_AndDrainsQueue()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync();

        var useCase = CreateUseCase(
            factory,
            source: new StaticSource(["https://varus.ua/kyiv/ovochi/item"]),
            extractor: new DelegatingExtractor(_ => Task.FromResult(ProductExtractResult.Success(
                new ProductCard("sku1", "Name", "https://varus.ua/kyiv/ovochi/item", "item", 10m, 12m, true, true, 1m,
                    "kg"),
                200,
                10,
                1.0d))));

        var result = await useCase.RunVegetablesAsync(CancellationToken.None);
        Assert.Equal("ok", result.Status);
        Assert.Equal(1, result.ProductsProcessed);
        Assert.Equal(0, result.Errors);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        Assert.Equal(1, await ScalarAsync(conn, "select count(*) from crawler_run"));
        Assert.Equal(1, await ScalarAsync(conn, "select count(*) from ingestion_run"));
        Assert.Equal(1, await ScalarAsync(conn, "select count(*) from price_snapshot"));
        Assert.Equal(1, await ScalarAsync(conn, "select count(*) from price_collect_queue where status='succeeded'"));
        Assert.Equal("ok", await StringScalarAsync(conn, "select status from crawler_run limit 1"));
        Assert.Equal(0,
            await ScalarAsync(conn,
                "select count(*) from price_collect_queue where status in ('pending','retry','reserved')"));
    }

    [Fact]
    public async Task StoreObservation_NewProduct_CreatesSnapshotAndUpdatesProductTimestamp()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync();

        var crawlerRepo = CreateCrawlerRunRepository(factory);
        var snapshotRepo = CreatePriceSnapshotRepository(factory);
        var runId = await crawlerRepo.StartAsync("integration", CancellationToken.None);

        var result = await snapshotRepo.StoreObservationAsync(
            runId,
            queueId: null,
            CreateObservation(oldPrice: 12m, price: 10m, promoFlag: true, inStock: true),
            CancellationToken.None);

        Assert.True(result.SnapshotCreated);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        Assert.Equal(1, await ScalarAsync(conn, "select count(*) from product"));
        Assert.Equal(1, await ScalarAsync(conn, "select count(*) from price_snapshot"));
        Assert.Equal(1, await ScalarAsync(conn, "select count(*) from product where updated_at is not null"));
    }

    [Fact]
    public async Task StoreObservation_UnchangedProduct_UpdatesOnlyUpdatedAt()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync();

        var crawlerRepo = CreateCrawlerRunRepository(factory);
        var snapshotRepo = CreatePriceSnapshotRepository(factory);
        var runId = await crawlerRepo.StartAsync("integration", CancellationToken.None);

        await snapshotRepo.StoreObservationAsync(
            runId,
            queueId: null,
            CreateObservation(oldPrice: 12m, price: 10m, promoFlag: true, inStock: true,
                observedAt: new DateTimeOffset(2026, 03, 10, 10, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);

        var second = await snapshotRepo.StoreObservationAsync(
            runId,
            queueId: null,
            CreateObservation(oldPrice: 12m, price: 10m, promoFlag: true, inStock: true,
                observedAt: new DateTimeOffset(2026, 03, 10, 11, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);

        Assert.False(second.SnapshotCreated);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        Assert.Equal(1, await ScalarAsync(conn, "select count(*) from price_snapshot"));
        Assert.Equal(
            new DateTime(2026, 03, 10, 11, 0, 0, DateTimeKind.Utc),
            await TimestampAsync(conn, "select updated_at from product limit 1"));
    }

    [Fact]
    public async Task StoreObservation_WhenOnlyOldPriceChanges_CreatesNewSnapshot()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync();

        var crawlerRepo = CreateCrawlerRunRepository(factory);
        var snapshotRepo = CreatePriceSnapshotRepository(factory);
        var runId = await crawlerRepo.StartAsync("integration", CancellationToken.None);

        await snapshotRepo.StoreObservationAsync(
            runId,
            queueId: null,
            CreateObservation(oldPrice: 12m, price: 10m, promoFlag: true, inStock: true),
            CancellationToken.None);

        var second = await snapshotRepo.StoreObservationAsync(
            runId,
            queueId: null,
            CreateObservation(oldPrice: 13m, price: 10m, promoFlag: true, inStock: true,
                observedAt: new DateTimeOffset(2026, 03, 10, 12, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);

        Assert.True(second.SnapshotCreated);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        Assert.Equal(2, await ScalarAsync(conn, "select count(*) from price_snapshot"));
        Assert.Equal(10m,
            await DecimalScalarAsync(conn, "select price from price_snapshot order by id desc limit 1"));
    }

    [Fact]
    public async Task StoreObservation_WhenOnlyFinalPriceChanges_CreatesNewSnapshot()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync();

        var crawlerRepo = CreateCrawlerRunRepository(factory);
        var snapshotRepo = CreatePriceSnapshotRepository(factory);
        var runId = await crawlerRepo.StartAsync("integration", CancellationToken.None);

        await snapshotRepo.StoreObservationAsync(
            runId,
            queueId: null,
            CreateObservation(oldPrice: 12m, price: 10m, promoFlag: true, inStock: true),
            CancellationToken.None);

        var second = await snapshotRepo.StoreObservationAsync(
            runId,
            queueId: null,
            CreateObservation(oldPrice: 12m, price: 9m, promoFlag: true, inStock: true,
                observedAt: new DateTimeOffset(2026, 03, 10, 12, 30, 0, TimeSpan.Zero)),
            CancellationToken.None);

        Assert.True(second.SnapshotCreated);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        Assert.Equal(2, await ScalarAsync(conn, "select count(*) from price_snapshot"));
        Assert.Equal(9m,
            await DecimalScalarAsync(conn, "select price from price_snapshot order by id desc limit 1"));
    }

    [Fact]
    public async Task StoreObservation_UsesDbRoutineAndPreservesNormalization()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync();

        var crawlerRepo = CreateCrawlerRunRepository(factory);
        var snapshotRepo = CreatePriceSnapshotRepository(factory);
        var runId = await crawlerRepo.StartAsync("integration", CancellationToken.None);
        var observedAt = new DateTimeOffset(2026, 03, 10, 13, 15, 0, TimeSpan.Zero);

        var result = await snapshotRepo.StoreObservationAsync(
            runId,
            queueId: null,
            new ProductObservation(
                $"   {new string('e', 90)}   ",
                $"   {new string('n', 600)}   ",
                $"   https://varus.ua/{new string('u', 1100)}   ",
                $"   {new string('s', 600)}   ",
                1.234567m,
                $"   {new string('p', 40)}   ",
                10m,
                12m,
                true,
                true,
                observedAt),
            CancellationToken.None);

        Assert.True(result.SnapshotCreated);
        Assert.NotNull(result.PriceSnapshotId);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        Assert.Equal(64,
            await ScalarAsync(conn, $"select length(external_id) from product where id={result.ProductId}"));
        Assert.Equal(512, await ScalarAsync(conn, $"select length(name) from product where id={result.ProductId}"));
        Assert.Equal(1024, await ScalarAsync(conn, $"select length(url) from product where id={result.ProductId}"));
        Assert.Equal(512, await ScalarAsync(conn, $"select length(slug) from product where id={result.ProductId}"));
        Assert.Equal(16, await ScalarAsync(conn, $"select length(pack_unit) from product where id={result.ProductId}"));
        Assert.Equal(observedAt.UtcDateTime,
            await TimestampAsync(conn, $"select updated_at from product where id={result.ProductId}"));
        Assert.Equal(result.PriceSnapshotId!.Value,
            await ScalarAsync(conn, $"select id from price_snapshot where id={result.PriceSnapshotId.Value}"));
    }

    [Fact]
    public async Task RunCrawlerUseCase_NonCriticalIssue_CreatesSnapshotAndLinkedCrawlError()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync();

        var useCase = CreateUseCase(
            factory,
            source: new StaticSource(["https://varus.ua/kyiv/ovochi/warn"]),
            extractor: new DelegatingExtractor(_ => Task.FromResult(ProductExtractResult.Partial(
                new ProductCard("sku-warn", "Warn", "https://varus.ua/kyiv/ovochi/warn", "warn", 10m, 12m, true, true,
                    1m,
                    "kg"),
                CrawlerErrorCodes.ParseFailed,
                200,
                "promo badge missing",
                10,
                1.0d))));

        var result = await useCase.RunVegetablesAsync(CancellationToken.None);
        Assert.Equal("ok", result.Status);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        Assert.Equal(1, await ScalarAsync(conn, "select count(*) from price_snapshot"));
        Assert.Equal(1, await ScalarAsync(conn, "select count(*) from crawl_error"));
        Assert.Equal(1, await ScalarAsync(conn, "select count(*) from crawl_error where product_id is not null"));
    }

    [Fact]
    public async Task QueueReservation_TwoWorkers_DoNotReserveSameItems()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync();

        var crawlerRepo = CreateCrawlerRunRepository(factory);
        var queueRepo = CreatePriceCollectQueueRepository(factory);
        var runId = await crawlerRepo.StartAsync("integration", CancellationToken.None);

        var items = Enumerable.Range(1, 10)
            .Select(i => new QueueEnqueueItem($"https://varus.ua/p/{i}", $"k-{i}"))
            .ToList();
        await queueRepo.EnqueueAsync(runId, items, maxAttempts: 3, CancellationToken.None);

        var lease = TimeSpan.FromSeconds(30);
        var firstTask =
            queueRepo.ReserveBatchAsync(runId, batchSize: 6, workerId: "worker-a", lease, CancellationToken.None);
        var secondTask =
            queueRepo.ReserveBatchAsync(runId, batchSize: 6, workerId: "worker-b", lease, CancellationToken.None);
        await Task.WhenAll(firstTask, secondTask);

        var first = firstTask.Result.Select(x => x.Id).ToHashSet();
        var second = secondTask.Result.Select(x => x.Id).ToHashSet();

        Assert.True(first.Count > 0);
        Assert.True(second.Count > 0);
        Assert.Empty(first.Intersect(second));
        Assert.Equal(10, first.Union(second).Count());
    }

    [Fact]
    public async Task QueueReaper_ReleasesExpiredReservations()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync();

        var crawlerRepo = CreateCrawlerRunRepository(factory);
        var queueRepo = CreatePriceCollectQueueRepository(factory);
        var runId = await crawlerRepo.StartAsync("integration", CancellationToken.None);

        await queueRepo.EnqueueAsync(
            runId,
            [new QueueEnqueueItem("https://varus.ua/p/reaper", "reaper-1")],
            maxAttempts: 3,
            CancellationToken.None);

        var firstReserve = await queueRepo.ReserveBatchAsync(runId, 1, "worker-a", TimeSpan.FromSeconds(1),
            CancellationToken.None);
        Assert.Single(firstReserve);

        await Task.Delay(1_200);
        var reaped = await queueRepo.ReapExpiredReservationsAsync(runId, CancellationToken.None);
        Assert.Equal(1, reaped);

        var secondReserve = await queueRepo.ReserveBatchAsync(runId, 1, "worker-b", TimeSpan.FromSeconds(30),
            CancellationToken.None);
        Assert.Single(secondReserve);
        Assert.Equal(firstReserve[0].Id, secondReserve[0].Id);
    }

    [Fact]
    public async Task RunCrawlerUseCase_WhenTransientFailuresExhausted_MarksDeadAndReturnsError()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync();

        var useCase = CreateUseCase(
            factory,
            source: new StaticSource(["https://varus.ua/kyiv/ovochi/fail"]),
            extractor: new DelegatingExtractor(_ => Task.FromResult(ProductExtractResult.Fail(
                CrawlerErrorCodes.Timeout,
                504,
                "timeout",
                10,
                1.0d,
                true))),
            queueOptions: new QueueOptions
            {
                BatchSize = 1,
                PollDelayMs = 1,
                LeaseSeconds = 5,
                MaxAttempts = 2,
                RetryBaseDelayMs = 1,
                RetryMaxDelayMs = 5,
                ReaperIntervalSeconds = 1
            });

        var result = await useCase.RunVegetablesAsync(CancellationToken.None);
        Assert.Equal("error", result.Status);
        Assert.Equal(0, result.ProductsProcessed);
        Assert.Equal(1, result.Errors);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        Assert.Equal(1, await ScalarAsync(conn, "select count(*) from price_collect_queue where status='dead'"));
        Assert.Equal(2, await ScalarAsync(conn, "select attempt from price_collect_queue limit 1"));
        Assert.Equal(1, await ScalarAsync(conn, "select count(*) from crawler_run where status='error'"));
        Assert.Equal(1, await ScalarAsync(conn, "select count(*) from crawl_error where product_id is null"));
    }

    [Fact]
    public async Task RunCrawlerUseCase_WhenFatalSourceError_ReturnsErrorAndMarksRunsError()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync();

        var useCase = CreateUseCase(
            factory,
            source: new ThrowingSource(),
            extractor: new DelegatingExtractor(_ => Task.FromResult(ProductExtractResult.Fail(
                CrawlerErrorCodes.Unknown,
                null,
                "should-not-be-used",
                0,
                0d,
                false))));

        var result = await useCase.RunVegetablesAsync(CancellationToken.None);
        Assert.Equal("error", result.Status);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        Assert.Equal(1, await ScalarAsync(conn, "select count(*) from crawler_run where status='error'"));
        Assert.Equal(1, await ScalarAsync(conn, "select count(*) from ingestion_run where status='error'"));
    }

    [Fact]
    public async Task SchemaBootstrapper_AppliesRoutineScripts_OncePerHash()
    {
        await PrepareSchemaAsync();

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        const string scriptName = "001__routine_support_text.sql";
        var firstAppliedAt = await TimestampAsync(
            conn,
            $"select applied_at from db_routine_script where script_name = '{scriptName}'");
        var firstHash = await StringScalarAsync(
            conn,
            $"select script_hash from db_routine_script where script_name = '{scriptName}'");

        await PrepareSchemaAsync();

        var secondAppliedAt = await TimestampAsync(
            conn,
            $"select applied_at from db_routine_script where script_name = '{scriptName}'");
        var secondHash = await StringScalarAsync(
            conn,
            $"select script_hash from db_routine_script where script_name = '{scriptName}'");

        Assert.Equal(1,
            await ScalarAsync(conn, $"select count(*) from db_routine_script where script_name = '{scriptName}'"));
        Assert.Equal(firstAppliedAt, secondAppliedAt);
        Assert.Equal(firstHash, secondHash);
        Assert.Equal(64, firstHash.Length);
    }

    [Fact]
    public async Task CrawlerRunRepository_StartAndFinish_UsesDbRoutines()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync();

        var crawlerRepo = CreateCrawlerRunRepository(factory);
        var runId = await crawlerRepo.StartAsync($"   {new string('s', 80)}   ", CancellationToken.None);
        await crawlerRepo.FinishAsync(runId, RunStatus.Error, $"   {new string('n', 300)}   ", CancellationToken.None);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        Assert.Equal(64, await ScalarAsync(conn, $"select length(source) from crawler_run where id={runId}"));
        Assert.Equal("error", await StringScalarAsync(conn, $"select status from crawler_run where id={runId}"));
        Assert.Equal(255, await ScalarAsync(conn, $"select length(note) from crawler_run where id={runId}"));
        Assert.Equal(1,
            await ScalarAsync(conn, $"select count(*) from crawler_run where id={runId} and finished_at is not null"));
    }

    [Fact]
    public async Task IngestionRunRepository_StartAndFinish_UsesDbRoutines()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync();

        var crawlerRepo = CreateCrawlerRunRepository(factory);
        var ingestionRepo = CreateIngestionRunRepository(factory);
        var runId = await crawlerRepo.StartAsync("integration", CancellationToken.None);
        var ingestionRunId = await ingestionRepo.StartAsync(runId, CancellationToken.None);

        await ingestionRepo.FinishAsync(
            ingestionRunId,
            RunStatus.Error,
            new ErrorInfo($"   {new string('E', 140)}   ", $"   {new string('M', 530)}   "),
            CancellationToken.None);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        Assert.Equal("error",
            await StringScalarAsync(conn, $"select status from ingestion_run where ingestion_run_id={ingestionRunId}"));
        Assert.Equal(128,
            await ScalarAsync(conn,
                $"select length(error_code) from ingestion_run where ingestion_run_id={ingestionRunId}"));
        Assert.Equal(512,
            await ScalarAsync(conn,
                $"select length(error_message) from ingestion_run where ingestion_run_id={ingestionRunId}"));
        Assert.Equal(1,
            await ScalarAsync(conn,
                $"select count(*) from ingestion_run where ingestion_run_id={ingestionRunId} and finished_at is not null"));
    }

    [Fact]
    public async Task InsertCrawlError_UsesDbRoutineAndPreservesNormalization()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync();

        var crawlerRepo = CreateCrawlerRunRepository(factory);
        var snapshotRepo = CreatePriceSnapshotRepository(factory);
        var runId = await crawlerRepo.StartAsync("integration", CancellationToken.None);
        var createdAt = new DateTimeOffset(2026, 03, 11, 7, 30, 0, TimeSpan.Zero);

        var errorId = await snapshotRepo.InsertCrawlErrorAsync(
            new CrawlErrorRecord(
                runId,
                QueueId: null,
                ProductId: null,
                Url: $"   https://varus.ua/{new string('u', 1100)}   ",
                CreatedAtUtc: createdAt,
                ErrorCode: "   ",
                HttpStatus: 504,
                ErrorMessage: $"   {new string('x', 600)}   "),
            CancellationToken.None);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        Assert.Equal("unknown",
            await StringScalarAsync(conn, $"select error_code from crawl_error where id={errorId}"));
        Assert.Equal(1024, await ScalarAsync(conn, $"select length(url) from crawl_error where id={errorId}"));
        Assert.Equal(512, await ScalarAsync(conn, $"select length(error_message) from crawl_error where id={errorId}"));
        Assert.Equal(createdAt.UtcDateTime,
            await TimestampAsync(conn, $"select created_at from crawl_error where id={errorId}"));
    }

    [Fact]
    public async Task QueueRepository_UsesDbRoutines_ForTransitionsAndStats()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync();

        var crawlerRepo = CreateCrawlerRunRepository(factory);
        var queueRepo = CreatePriceCollectQueueRepository(factory);
        var runId = await crawlerRepo.StartAsync("integration", CancellationToken.None);

        var inserted = await queueRepo.EnqueueAsync(
            runId,
            [
                new QueueEnqueueItem($"   https://varus.ua/{new string('a', 1100)}   ",
                    $"   {new string('k', 140)}   "),
                new QueueEnqueueItem("https://varus.ua/second", "second-key"),
                new QueueEnqueueItem("https://varus.ua/third", "third-key")
            ],
            maxAttempts: 0,
            CancellationToken.None);

        Assert.Equal(3, inserted);

        var reserved = await queueRepo.ReserveBatchAsync(
            runId,
            batchSize: 2,
            workerId: $"   {new string('w', 150)}   ",
            leaseDuration: TimeSpan.FromSeconds(15),
            CancellationToken.None);

        Assert.Equal(2, reserved.Count);

        await queueRepo.MarkSucceededAsync(reserved[0].Id, CancellationToken.None);
        await queueRepo.MarkRetryAsync(
            reserved[1].Id,
            new string('r', 100),
            429,
            new string('m', 600),
            new DateTimeOffset(2026, 03, 11, 8, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        var reservedAfterRetry = await queueRepo.ReserveBatchAsync(
            runId,
            batchSize: 5,
            workerId: "worker-b",
            leaseDuration: TimeSpan.FromSeconds(15),
            CancellationToken.None);

        foreach (var item in reservedAfterRetry)
        {
            await queueRepo.MarkDeadAsync(
                item.Id,
                new string('f', 100),
                500,
                new string('e', 600),
                CancellationToken.None);
        }

        Assert.False(await queueRepo.HasOutstandingItemsAsync(runId, CancellationToken.None));

        var stats = await queueRepo.GetRunStatsAsync(runId, CancellationToken.None);
        Assert.Equal(new QueueRunStats(0, 0, 0, 1, 2), stats);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        Assert.Equal(1024, await ScalarAsync(conn, "select length(url) from price_collect_queue order by id limit 1"));
        Assert.Equal(128,
            await ScalarAsync(conn, "select length(idempotency_key) from price_collect_queue order by id limit 1"));
        Assert.Equal(1, await ScalarAsync(conn, "select max_attempts from price_collect_queue order by id limit 1"));
        Assert.Equal(64,
            await ScalarAsync(conn,
                "select max(length(last_error_code)) from price_collect_queue where status='dead'"));
        Assert.Equal(512,
            await ScalarAsync(conn,
                "select max(length(last_error_message)) from price_collect_queue where status='dead'"));
        Assert.Equal(1,
            await ScalarAsync(conn,
                "select count(*) from price_collect_queue where status='succeeded' and finished_at is not null"));
        Assert.Equal(2, await ScalarAsync(conn, "select count(*) from price_collect_queue where status='dead'"));
    }

    [Fact]
    public async Task PgRoutineExecutor_CallsSupportFunction()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync();

        var executor = new PgRoutineExecutor(factory);
        var trimmed = await executor.ExecuteScalarAsync<string?>(
            DbRoutineCall.ScalarFunction("routine_support_trim_nullable")
                .AddParameter("p_value", "  abcdef  ")
                .AddParameter("p_max_length", 4),
            CancellationToken.None);
        var empty = await executor.ExecuteScalarAsync<string?>(
            DbRoutineCall.ScalarFunction("routine_support_trim_nullable")
                .AddParameter("p_value", "   ")
                .AddParameter("p_max_length", 4),
            CancellationToken.None);

        Assert.Equal("abcd", trimmed);
        Assert.Null(empty);
    }

    [Fact]
    public void DbRoutineCall_RendersNamedNotation_ForSupportedModes()
    {
        var scalar = DbRoutineCall.ScalarFunction("crawler_run_start")
            .AddParameter("p_source", "integration")
            .ToCommandText();
        var setReturning = DbRoutineCall.SetReturningFunction("price_collect_queue_reserve_batch")
            .AddParameter("p_run_id", 42L)
            .AddParameter("p_batch_size", 10)
            .ToCommandText();
        var procedure = DbRoutineCall.Procedure("crawler_run_finish")
            .AddParameter("p_run_id", 42L)
            .AddParameter("p_status", "ok")
            .ToCommandText();

        Assert.Equal("select crawler_run_start(p_source => @p_source);", scalar);
        Assert.Equal(
            "select * from price_collect_queue_reserve_batch(p_run_id => @p_run_id, p_batch_size => @p_batch_size);",
            setReturning);
        Assert.Equal("call crawler_run_finish(p_run_id => @p_run_id, p_status => @p_status);", procedure);
    }

    private static RunCrawlerUseCase CreateUseCase(
        IPgConnectionFactory factory,
        IProductUrlSource source,
        IProductCardExtractor extractor,
        QueueOptions? queueOptions = null)
        => new(
            Options.Create(new CrawlerOptions
            {
                SitemapIndexUrl = "unused",
                VegetablesUrlContains = "ovochi",
                MaxProductsPerRun = 10,
                MaxUrls = 10,
                MaxConcurrency = 2
            }),
            Options.Create(queueOptions ?? new QueueOptions
            {
                BatchSize = 4,
                PollDelayMs = 1,
                LeaseSeconds = 10,
                MaxAttempts = 3,
                RetryBaseDelayMs = 1,
                RetryMaxDelayMs = 10,
                ReaperIntervalSeconds = 1
            }),
            Options.Create(new UrlFilterOptions()),
            source,
            extractor,
            CreateCrawlerRunRepository(factory),
            CreateIngestionRunRepository(factory),
            CreatePriceCollectQueueRepository(factory),
            CreatePriceSnapshotRepository(factory),
            NullLogger<RunCrawlerUseCase>.Instance);

    private static ProductObservation CreateObservation(
        decimal? oldPrice,
        decimal? price,
        bool promoFlag,
        bool inStock,
        DateTimeOffset? observedAt = null)
        => new(
            "sku-1",
            "Name",
            "https://varus.ua/kyiv/ovochi/item",
            "item",
            1m,
            "kg",
            price,
            oldPrice,
            promoFlag,
            inStock,
            observedAt ?? new DateTimeOffset(2026, 03, 10, 9, 0, 0, TimeSpan.Zero));

    private static PgConnectionFactory CreateFactory()
        => new(new SelectedDatabase(DatabaseTarget.Dev, ConnectionString, "varprice"));

    private static PgCrawlerRunRepository CreateCrawlerRunRepository(IPgConnectionFactory factory)
        => new(new PgRoutineExecutor(factory));

    private static PgIngestionRunRepository CreateIngestionRunRepository(IPgConnectionFactory factory)
        => new(new PgRoutineExecutor(factory));

    private static PgPriceSnapshotRepository CreatePriceSnapshotRepository(IPgConnectionFactory factory)
        => new(new PgRoutineExecutor(factory));

    private static PgPriceCollectQueueRepository CreatePriceCollectQueueRepository(IPgConnectionFactory factory)
        => new(new PgRoutineExecutor(factory));

    private static async Task PrepareSchemaAsync()
    {
        await using var dbContext = CreateDbContext();
        var schema = new SchemaBootstrapper(dbContext, NullLogger<SchemaBootstrapper>.Instance);
        await schema.EnsureSchemaAsync();

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "truncate table crawl_error, price_snapshot, price_collect_queue, product, ingestion_run, crawler_run restart identity cascade;",
            conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static VarPriceDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<VarPriceDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new VarPriceDbContext(options);
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(value);
    }

    private static async Task<decimal> DecimalScalarAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return Convert.ToDecimal(value);
    }

    private static async Task<string> StringScalarAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return Convert.ToString(value)!;
    }

    private static async Task<DateTime> TimestampAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return Convert.ToDateTime(value);
    }

    private sealed class StaticSource(IReadOnlyList<string> urls) : IProductUrlSource
    {
        public Task<IReadOnlyList<string>> GetProductUrlsAsync(string sitemapIndexUrl, CancellationToken ct) =>
            Task.FromResult(urls);
    }

    private sealed class ThrowingSource : IProductUrlSource
    {
        public Task<IReadOnlyList<string>> GetProductUrlsAsync(string sitemapIndexUrl, CancellationToken ct)
            => throw new InvalidOperationException("fatal source error");
    }

    private sealed class DelegatingExtractor(Func<string, Task<ProductExtractResult>> handler) : IProductCardExtractor
    {
        public Task<ProductExtractResult> ExtractAsync(string url, CancellationToken ct) => handler(url);
    }
}
