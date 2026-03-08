using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Npgsql;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;
using VarPrice.Application.UseCases;
using VarPrice.Domain.Models;
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
        await PrepareSchemaAsync(factory);

        var useCase = CreateUseCase(
            factory,
            source: new StaticSource(["https://varus.ua/kyiv/ovochi/item"]),
            extractor: new DelegatingExtractor(_ => Task.FromResult(ProductExtractResult.Success(
                new ProductCard("sku1", "Name", "https://varus.ua/kyiv/ovochi/item", 12m, null, false, true, 1m, "kg",
                    "kyiv"),
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
        Assert.Equal(0,
            await ScalarAsync(conn,
                "select count(*) from price_collect_queue where status in ('pending','retry','reserved')"));
    }

    [Fact]
    public async Task QueueReservation_TwoWorkers_DoNotReserveSameItems()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync(factory);

        var crawlerRepo = new PgCrawlerRunRepository(factory);
        var queueRepo = new PgPriceCollectQueueRepository(factory);
        var runId = await crawlerRepo.StartAsync("integration", CancellationToken.None);

        var items = Enumerable.Range(1, 10)
            .Select(i => new QueueEnqueueItem($"https://varus.ua/p/{i}", null, $"k-{i}"))
            .ToList();
        await queueRepo.EnqueueAsync(runId, items, maxAttempts: 3, CancellationToken.None);

        var lease = TimeSpan.FromSeconds(30);
        var firstTask =
            queueRepo.ReserveBatchAsync(runId, batchSize: 6, workerId: "worker-a", lease, CancellationToken.None);
        var secondTask =
            queueRepo.ReserveBatchAsync(runId, batchSize: 6, workerId: "worker-b", lease, CancellationToken.None);
        await Task.WhenAll(firstTask, secondTask);

        var first = firstTask.Result.Select(x => x.QueueId).ToHashSet();
        var second = secondTask.Result.Select(x => x.QueueId).ToHashSet();

        Assert.True(first.Count > 0);
        Assert.True(second.Count > 0);
        Assert.Empty(first.Intersect(second));
        Assert.Equal(10, first.Union(second).Count());
    }

    [Fact]
    public async Task QueueReaper_ReleasesExpiredReservations()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync(factory);

        var crawlerRepo = new PgCrawlerRunRepository(factory);
        var queueRepo = new PgPriceCollectQueueRepository(factory);
        var runId = await crawlerRepo.StartAsync("integration", CancellationToken.None);

        await queueRepo.EnqueueAsync(
            runId,
            [new QueueEnqueueItem("https://varus.ua/p/reaper", null, "reaper-1")],
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
        Assert.Equal(firstReserve[0].QueueId, secondReserve[0].QueueId);
    }

    [Fact]
    public async Task Persistence_IsIdempotent_ForSnapshotAndProductError_ByQueueId()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync(factory);

        var crawlerRepo = new PgCrawlerRunRepository(factory);
        var queueRepo = new PgPriceCollectQueueRepository(factory);
        var snapshotRepo = new PgPriceSnapshotRepository(factory);
        var runId = await crawlerRepo.StartAsync("integration", CancellationToken.None);

        await queueRepo.EnqueueAsync(
            runId,
            [new QueueEnqueueItem("https://varus.ua/p/idempotent", null, "idem-1")],
            maxAttempts: 3,
            CancellationToken.None);
        var reserved = await queueRepo.ReserveBatchAsync(runId, 1, "worker", TimeSpan.FromSeconds(30),
            CancellationToken.None);
        var item = Assert.Single(reserved);

        var productKey = await snapshotRepo.UpsertProductAsync("idem-sku", "Idem Product",
            "https://varus.ua/p/idempotent",
            1m, "kg", CancellationToken.None);

        await snapshotRepo.InsertSnapshotAsync(runId, productKey, "kyiv", 100m, 120m, true, true, item.QueueId,
            CancellationToken.None);
        await snapshotRepo.InsertSnapshotAsync(runId, productKey, "kyiv", 95m, 120m, true, true, item.QueueId,
            CancellationToken.None);

        await snapshotRepo.InsertProductErrorAsync(runId, item.QueueId, "https://varus.ua/p/idempotent",
            CrawlerErrorCodes.Timeout, 504, "timeout-1", CancellationToken.None);
        await snapshotRepo.InsertProductErrorAsync(runId, item.QueueId, "https://varus.ua/p/idempotent",
            CrawlerErrorCodes.Http5xx, 503, "timeout-2", CancellationToken.None);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        Assert.Equal(1, await ScalarAsync(conn, $"select count(*) from price_snapshot where queue_id={item.QueueId}"));
        Assert.Equal(95m, await DecimalScalarAsync(conn,
            $"select price from price_snapshot where queue_id={item.QueueId}"));
        Assert.Equal(1, await ScalarAsync(conn, $"select count(*) from product_errors where queue_id={item.QueueId}"));
        Assert.Equal("http_5xx", await StringScalarAsync(conn,
            $"select error_code from product_errors where queue_id={item.QueueId}"));
    }

    [Fact]
    public async Task RunCrawlerUseCase_WhenTransientFailuresExhausted_MarksDead()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync(factory);

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
        Assert.Equal("ok", result.Status);
        Assert.Equal(0, result.ProductsProcessed);
        Assert.Equal(1, result.Errors);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        Assert.Equal(1, await ScalarAsync(conn, "select count(*) from price_collect_queue where status='dead'"));
        Assert.Equal(2, await ScalarAsync(conn, "select attempt from price_collect_queue limit 1"));
    }

    [Fact]
    public async Task RunCrawlerUseCase_WhenFatalSourceError_ReturnsErrorAndMarksRunsFailed()
    {
        var factory = CreateFactory();
        await PrepareSchemaAsync(factory);

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

        Assert.Equal(1, await ScalarAsync(conn, "select count(*) from crawler_run where status='failed'"));
        Assert.Equal(1, await ScalarAsync(conn, "select count(*) from ingestion_run where status='failed'"));
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
            new PgCrawlerRunRepository(factory),
            new PgIngestionRunRepository(factory),
            new PgPriceCollectQueueRepository(factory),
            new PgPriceSnapshotRepository(factory),
            NullLogger<RunCrawlerUseCase>.Instance);

    private static PgConnectionFactory CreateFactory()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = ConnectionString })
            .Build();
        return new PgConnectionFactory(config);
    }

    private static async Task PrepareSchemaAsync(IPgConnectionFactory factory)
    {
        var schema = new SchemaBootstrapper(factory, NullLogger<SchemaBootstrapper>.Instance);
        await schema.EnsureSchemaAsync();

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "truncate table price_snapshot, product_errors, price_collect_queue, product, ingestion_run, crawler_run restart identity cascade;",
            conn);
        await cmd.ExecuteNonQueryAsync();
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
        return Convert.ToString(value) ?? string.Empty;
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
