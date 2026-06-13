using System.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using VarPrice.Domain.Models;
using VarPrice.Infrastructure.Persistence;

namespace VarPrice.Web.Tests;

public sealed class ProductCatalogRepositoryTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpsertDiscoveredAsync_EmptyInput_ReturnsZeroResult()
    {
        var sut = new PgProductCatalogRepository(new PgRoutineExecutor(new ThrowingConnectionFactory()));

        var result = await sut.UpsertDiscoveredAsync([], CancellationToken.None);

        Assert.Equal(new ProductCatalogUpsertResult(0, 0, 0), result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void UpsertDiscoveredAsync_InvalidItems_AreSkipped()
    {
        var prepared = ProductCatalogBatchPreparer.Prepare(
        [
            new ProductCatalogUpsertItem("", "https://example/a", "https://example/a", null, null, Now(1)),
            new ProductCatalogUpsertItem("varus", " ", "https://example/b", null, null, Now(2)),
            new ProductCatalogUpsertItem("varus", "https://example/c", "\t", null, null, Now(3)),
            new ProductCatalogUpsertItem("varus", "https://example/d", "https://example/d", null, null, Now(4))
        ]);

        var item = Assert.Single(prepared);
        Assert.Equal("varus", item.Source);
        Assert.Equal("https://example/d", item.NormalizedUrl);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void UpsertDiscoveredAsync_DuplicateSourceAndUrl_AreDeduplicated()
    {
        var prepared = ProductCatalogBatchPreparer.Prepare(
        [
            new ProductCatalogUpsertItem("VARUS", "https://example/old", "HTTPS://EXAMPLE/A", null, null, Now(1)),
            new ProductCatalogUpsertItem("varus", "https://example/new", "https://example/a", null, null, Now(3)),
            new ProductCatalogUpsertItem("varus", "https://example/middle", "https://example/a", null, null, Now(2))
        ]);

        var item = Assert.Single(prepared);
        Assert.Equal("https://example/new", item.Url);
        Assert.Equal(Now(3), item.DiscoveredAtUtc);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void UpsertDiscoveredAsync_TrimsStringValues()
    {
        var prepared = ProductCatalogBatchPreparer.Prepare(
        [
            new ProductCatalogUpsertItem(
                " varus ",
                " https://example/a ",
                " https://example/a ",
                " sku ",
                " slug ",
                Now(1))
        ]);

        var item = Assert.Single(prepared);
        Assert.Equal("varus", item.Source);
        Assert.Equal("https://example/a", item.Url);
        Assert.Equal("https://example/a", item.NormalizedUrl);
        Assert.Equal("sku", item.ExternalId);
        Assert.Equal("slug", item.Slug);
    }

    private static DateTimeOffset Now(int day)
        => new(2026, 06, day, 10, 0, 0, TimeSpan.Zero);

    private sealed class ThrowingConnectionFactory : IPgConnectionFactory
    {
        public IDbConnection Create() => throw new InvalidOperationException("Database should not be touched.");
    }
}

[Collection(PostgresIntegrationCollection.Name)]
public sealed class ProductCatalogRepositoryIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetByIdAsync_ExistingRow_MapsAllFields()
    {
        var repo = await CreatePreparedRepositoryAsync();
        var discoveredAt = Now(1);

        await repo.UpsertDiscoveredAsync(
        [
            new ProductCatalogUpsertItem("varus", "https://example/a", "https://example/a", "sku", "slug",
                discoveredAt)
        ], CancellationToken.None);

        await using var conn = new NpgsqlConnection(PostgresIntegrationFixture.ConnectionString);
        await conn.OpenAsync();
        await ExecuteAsync(
            conn,
            """
            update product_catalog
            set last_checked_at = '2026-06-02T10:00:00Z',
                next_check_at = '2026-06-03T10:00:00Z',
                consecutive_errors = 2
            where source = 'varus';
            """);
        var id = await ScalarLongAsync(conn, "select id from product_catalog where source = 'varus';");

        var item = await repo.GetByIdAsync(id, CancellationToken.None);

        Assert.NotNull(item);
        Assert.Equal(id, item.Id);
        Assert.Equal("varus", item.Source);
        Assert.Equal("https://example/a", item.Url);
        Assert.Equal("https://example/a", item.NormalizedUrl);
        Assert.Equal("sku", item.ExternalId);
        Assert.Equal("slug", item.Slug);
        Assert.Equal(discoveredAt, item.FirstDiscoveredAtUtc);
        Assert.Equal(discoveredAt, item.LastDiscoveredAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 06, 02, 10, 0, 0, TimeSpan.Zero), item.LastCheckedAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 06, 03, 10, 0, 0, TimeSpan.Zero), item.NextCheckAtUtc);
        Assert.True(item.IsActive);
        Assert.Equal(2, item.ConsecutiveErrors);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetByIdAsync_MissingRow_ReturnsNull()
    {
        var repo = await CreatePreparedRepositoryAsync();

        var item = await repo.GetByIdAsync(9_999_999, CancellationToken.None);

        Assert.Null(item);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpsertDiscoveredAsync_NewUrl_InsertsCatalogItem()
    {
        var repo = await CreatePreparedRepositoryAsync();
        var discoveredAt = Now(1);

        var result = await repo.UpsertDiscoveredAsync(
        [
            new ProductCatalogUpsertItem("varus", "https://example/a", "https://example/a", null, null,
                discoveredAt)
        ], CancellationToken.None);

        Assert.Equal(new ProductCatalogUpsertResult(1, 1, 0), result);
        await using var conn = new NpgsqlConnection(PostgresIntegrationFixture.ConnectionString);
        await conn.OpenAsync();
        Assert.Equal(1, await ScalarLongAsync(conn, "select count(*) from product_catalog;"));
        Assert.Equal(0, await ScalarLongAsync(conn, "select consecutive_errors from product_catalog limit 1;"));
        Assert.True(await ScalarBoolAsync(conn, "select is_active from product_catalog limit 1;"));
        Assert.Equal(discoveredAt.UtcDateTime,
            await TimestampAsync(conn, "select first_discovered_at from product_catalog limit 1;"));
        Assert.Equal(discoveredAt.UtcDateTime,
            await TimestampAsync(conn, "select last_discovered_at from product_catalog limit 1;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpsertDiscoveredAsync_ExistingUrl_UpdatesLastDiscoveredOnly()
    {
        var repo = await CreatePreparedRepositoryAsync();
        var first = Now(1);
        var second = Now(5);

        await repo.UpsertDiscoveredAsync(
        [
            new ProductCatalogUpsertItem("varus", "https://example/a", "https://example/a", "sku", "slug", first)
        ], CancellationToken.None);

        await using var conn = new NpgsqlConnection(PostgresIntegrationFixture.ConnectionString);
        await conn.OpenAsync();
        await ExecuteAsync(
            conn,
            """
            update product_catalog
            set last_checked_at = '2026-06-02T10:00:00Z',
                consecutive_errors = 3
            where source = 'varus';
            """);

        var result = await repo.UpsertDiscoveredAsync(
        [
            new ProductCatalogUpsertItem("varus", "https://example/a-new", "https://example/a", null, null, second)
        ], CancellationToken.None);

        Assert.Equal(new ProductCatalogUpsertResult(1, 0, 1), result);
        Assert.Equal(1, await ScalarLongAsync(conn, "select count(*) from product_catalog;"));
        Assert.Equal(first.UtcDateTime,
            await TimestampAsync(conn, "select first_discovered_at from product_catalog limit 1;"));
        Assert.Equal(second.UtcDateTime,
            await TimestampAsync(conn, "select last_discovered_at from product_catalog limit 1;"));
        Assert.Equal(new DateTime(2026, 06, 02, 10, 0, 0, DateTimeKind.Utc),
            await TimestampAsync(conn, "select last_checked_at from product_catalog limit 1;"));
        Assert.Equal(3, await ScalarLongAsync(conn, "select consecutive_errors from product_catalog limit 1;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProductCatalog_SameSourceAndNormalizedUrl_CannotDuplicate()
    {
        _ = await CreatePreparedRepositoryAsync();
        await using var conn = new NpgsqlConnection(PostgresIntegrationFixture.ConnectionString);
        await conn.OpenAsync();

        await ExecuteAsync(
            conn,
            """
            insert into product_catalog(source, url, normalized_url, first_discovered_at, last_discovered_at)
            values('varus', 'https://example/a', 'https://example/a', now(), now());
            """);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            conn,
            """
            insert into product_catalog(source, url, normalized_url, first_discovered_at, last_discovered_at)
            values('varus', 'https://example/a-copy', 'https://example/a', now(), now());
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProductCatalog_SameNormalizedUrlDifferentSources_AreAllowed()
    {
        var repo = await CreatePreparedRepositoryAsync();

        var result = await repo.UpsertDiscoveredAsync(
        [
            new ProductCatalogUpsertItem("varus", "https://example/a", "https://example/a", null, null, Now(1)),
            new ProductCatalogUpsertItem("other", "https://example/a", "https://example/a", null, null, Now(1))
        ], CancellationToken.None);

        Assert.Equal(new ProductCatalogUpsertResult(2, 2, 0), result);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpsertDiscoveredAsync_InactiveExistingItem_ReactivatesItem()
    {
        var repo = await CreatePreparedRepositoryAsync();
        await repo.UpsertDiscoveredAsync(
        [
            new ProductCatalogUpsertItem("varus", "https://example/a", "https://example/a", null, null, Now(1))
        ], CancellationToken.None);

        await using var conn = new NpgsqlConnection(PostgresIntegrationFixture.ConnectionString);
        await conn.OpenAsync();
        await ExecuteAsync(conn, "update product_catalog set is_active = false;");

        await repo.UpsertDiscoveredAsync(
        [
            new ProductCatalogUpsertItem("varus", "https://example/a", "https://example/a", null, null, Now(2))
        ], CancellationToken.None);

        Assert.True(await ScalarBoolAsync(conn, "select is_active from product_catalog limit 1;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpsertDiscoveredAsync_MultipleItems_InsertsInSingleBatch()
    {
        var repo = await CreatePreparedRepositoryAsync();

        await repo.UpsertDiscoveredAsync(
        [
            new ProductCatalogUpsertItem("varus", "https://example/a", "https://example/a", null, null, Now(1))
        ], CancellationToken.None);

        var result = await repo.UpsertDiscoveredAsync(
        [
            new ProductCatalogUpsertItem("varus", "https://example/a2", "https://example/a", null, null, Now(2)),
            new ProductCatalogUpsertItem("varus", "https://example/b", "https://example/b", null, null, Now(2)),
            new ProductCatalogUpsertItem("varus", "https://example/c", "https://example/c", null, null, Now(2))
        ], CancellationToken.None);

        Assert.Equal(new ProductCatalogUpsertResult(3, 2, 1), result);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpsertDiscoveredAsync_NullExternalIdAndSlug_DoNotEraseExistingValues()
    {
        var repo = await CreatePreparedRepositoryAsync();

        await repo.UpsertDiscoveredAsync(
        [
            new ProductCatalogUpsertItem("varus", "https://example/a", "https://example/a", "sku", "slug", Now(1))
        ], CancellationToken.None);
        await repo.UpsertDiscoveredAsync(
        [
            new ProductCatalogUpsertItem("varus", "https://example/a", "https://example/a", null, " ", Now(2))
        ], CancellationToken.None);

        var item = await repo.GetBySourceAndNormalizedUrlAsync("varus", "https://example/a", CancellationToken.None);
        Assert.NotNull(item);
        Assert.Equal("sku", item.ExternalId);
        Assert.Equal("slug", item.Slug);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDueProductsAsync_ReturnsOldestFirstAndExcludesInactiveFutureRows()
    {
        var repo = await CreatePreparedRepositoryAsync();
        await SeedCatalogRowsAsync();

        var due = await repo.GetDueProductsAsync(
            3,
            new DateTimeOffset(2026, 06, 12, 10, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(30),
            "test-worker",
            CancellationToken.None);

        Assert.Equal(["https://example/a", "https://example/b", "https://example/c"],
            due.Select(x => x.NormalizedUrl).ToArray());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDueProductsAsync_SameTimestamps_AreOrderedById()
    {
        var repo = await CreatePreparedRepositoryAsync();
        await using var conn = new NpgsqlConnection(PostgresIntegrationFixture.ConnectionString);
        await conn.OpenAsync();
        await ExecuteAsync(
            conn,
            """
            insert into product_catalog(source, url, normalized_url, first_discovered_at, last_discovered_at, last_checked_at, next_check_at, is_active)
            values
                ('varus', 'https://example/c', 'https://example/c', now(), now(), '2026-06-01T10:00:00Z', '2026-06-12T09:00:00Z', true),
                ('varus', 'https://example/a', 'https://example/a', now(), now(), '2026-06-01T10:00:00Z', '2026-06-12T09:00:00Z', true),
                ('varus', 'https://example/b', 'https://example/b', now(), now(), '2026-06-01T10:00:00Z', '2026-06-12T09:00:00Z', true);
            """);

        var due = await repo.GetDueProductsAsync(
            3,
            new DateTimeOffset(2026, 06, 12, 10, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(30),
            "test-worker",
            CancellationToken.None);

        Assert.Equal(due.OrderBy(x => x.Id).Select(x => x.Id), due.Select(x => x.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDueProductsAsync_ActiveReservationIsSkippedAndExpiredReservationIsReused()
    {
        var repo = await CreatePreparedRepositoryAsync();
        await SeedCatalogRowsAsync();
        await using var conn = new NpgsqlConnection(PostgresIntegrationFixture.ConnectionString);
        await conn.OpenAsync();
        await ExecuteAsync(
            conn,
            """
            update product_catalog
            set reserved_until = '2026-06-12T11:00:00Z'
            where normalized_url = 'https://example/a';

            update product_catalog
            set reserved_until = '2026-06-12T09:00:00Z'
            where normalized_url = 'https://example/b';
            """);

        var due = await repo.GetDueProductsAsync(
            2,
            new DateTimeOffset(2026, 06, 12, 10, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(30),
            "test-worker",
            CancellationToken.None);

        Assert.Equal(["https://example/b", "https://example/c"], due.Select(x => x.NormalizedUrl).ToArray());
        Assert.All(due, item => Assert.NotEqual("https://example/a", item.NormalizedUrl));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDueProductsAsync_SequentialWorkersDoNotReceiveSameCatalogItems()
    {
        var repo = await CreatePreparedRepositoryAsync();
        await SeedCatalogRowsAsync();
        var now = new DateTimeOffset(2026, 06, 12, 10, 0, 0, TimeSpan.Zero);

        var first = await repo.GetDueProductsAsync(2, now, TimeSpan.FromMinutes(30), "worker-1",
            CancellationToken.None);
        var second =
            await repo.GetDueProductsAsync(2, now, TimeSpan.FromMinutes(30), "worker-2", CancellationToken.None);

        Assert.Empty(first.Select(x => x.Id).Intersect(second.Select(x => x.Id)));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MarkCheckedAsync_UpdatesSchedulingStateAndClearsReservation()
    {
        var repo = await CreatePreparedRepositoryAsync();
        await SeedCatalogRowsAsync();
        var selected = Assert.Single(await repo.GetDueProductsAsync(
            1,
            new DateTimeOffset(2026, 06, 12, 10, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(30),
            "test-worker",
            CancellationToken.None));

        await repo.MarkCheckedAsync(
            new ProductCatalogCheckSuccess(
                selected.Id,
                new DateTimeOffset(2026, 06, 12, 10, 5, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 06, 13, 10, 5, 0, TimeSpan.Zero),
                "sku-new",
                "slug-new"),
            CancellationToken.None);

        await using var conn = new NpgsqlConnection(PostgresIntegrationFixture.ConnectionString);
        await conn.OpenAsync();
        Assert.Equal(0, await ScalarLongAsync(conn, "select consecutive_errors from product_catalog where id = 1;"));
        Assert.Equal("sku-new", await ScalarStringAsync(conn, "select external_id from product_catalog where id = 1;"));
        Assert.Equal(1, await ScalarLongAsync(conn,
            "select count(*) from product_catalog where id = 1 and reserved_at is null and reserved_until is null and reserved_by is null;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MarkFailedAsync_IncrementsErrorsSchedulesRetryAndClearsReservation()
    {
        var repo = await CreatePreparedRepositoryAsync();
        await SeedCatalogRowsAsync();
        var selected = Assert.Single(await repo.GetDueProductsAsync(
            1,
            new DateTimeOffset(2026, 06, 12, 10, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(30),
            "test-worker",
            CancellationToken.None));

        await repo.MarkFailedAsync(
            new ProductCatalogCheckFailure(
                selected.Id,
                new DateTimeOffset(2026, 06, 12, 10, 5, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 06, 12, 11, 5, 0, TimeSpan.Zero)),
            CancellationToken.None);

        await using var conn = new NpgsqlConnection(PostgresIntegrationFixture.ConnectionString);
        await conn.OpenAsync();
        Assert.Equal(1, await ScalarLongAsync(conn, "select consecutive_errors from product_catalog where id = 1;"));
        Assert.Equal(1, await ScalarLongAsync(conn,
            "select count(*) from product_catalog where id = 1 and reserved_at is null and reserved_until is null and reserved_by is null;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PriceCollectQueue_StoresProductCatalogId()
    {
        var repo = await CreatePreparedRepositoryAsync();
        await repo.UpsertDiscoveredAsync(
        [
            new ProductCatalogUpsertItem("varus", "https://example/a", "https://example/a", null, null, Now(1))
        ], CancellationToken.None);
        await using var conn = new NpgsqlConnection(PostgresIntegrationFixture.ConnectionString);
        await conn.OpenAsync();
        await ExecuteAsync(conn, "insert into crawler_run(status, source) values('running', 'test');");
        var catalogId = await ScalarLongAsync(conn, "select id from product_catalog limit 1;");
        var queueRepo = new PgPriceCollectQueueRepository(new PgRoutineExecutor(CreateFactory()));

        await queueRepo.EnqueueAsync(
            1,
            [new QueueEnqueueItem("https://example/a", "test-key", catalogId)],
            3,
            CancellationToken.None);

        Assert.Equal(catalogId,
            await ScalarLongAsync(conn, "select product_catalog_id from price_collect_queue limit 1;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SchemaBootstrapper_ProductCatalogSchema_IsIdempotent()
    {
        await PrepareSchemaAsync();
        await PrepareSchemaAsync();

        await using var conn = new NpgsqlConnection(PostgresIntegrationFixture.ConnectionString);
        await conn.OpenAsync();
        Assert.Equal(1,
            await ScalarLongAsync(conn,
                "select count(*) from db_routine_script where script_name = '040__product_catalog_routines.sql';"));
    }

    private static async Task<PgProductCatalogRepository> CreatePreparedRepositoryAsync()
    {
        await PrepareSchemaAsync();
        return new PgProductCatalogRepository(new PgRoutineExecutor(CreateFactory()));
    }

    private static async Task PrepareSchemaAsync()
    {
        await using var dbContext = CreateDbContext();
        var schema = new SchemaBootstrapper(dbContext, NullLogger<SchemaBootstrapper>.Instance);
        await schema.EnsureSchemaAsync();

        await using var conn = new NpgsqlConnection(PostgresIntegrationFixture.ConnectionString);
        await conn.OpenAsync();
        await ExecuteAsync(
            conn,
            "truncate table crawl_error, price_snapshot, price_collect_queue, product_catalog, product, ingestion_run, crawler_run restart identity cascade;");
    }

    private static async Task SeedCatalogRowsAsync()
    {
        await using var conn = new NpgsqlConnection(PostgresIntegrationFixture.ConnectionString);
        await conn.OpenAsync();
        await ExecuteAsync(
            conn,
            """
            insert into product_catalog(source, url, normalized_url, first_discovered_at, last_discovered_at, last_checked_at, next_check_at, is_active)
            values
                ('varus', 'https://example/a', 'https://example/a', now(), now(), null, null, true),
                ('varus', 'https://example/b', 'https://example/b', now(), now(), '2026-06-02T10:00:00Z', null, true),
                ('varus', 'https://example/c', 'https://example/c', now(), now(), '2026-06-11T10:00:00Z', null, true),
                ('varus', 'https://example/d', 'https://example/d', now(), now(), '2026-06-01T10:00:00Z', null, false),
                ('varus', 'https://example/e', 'https://example/e', now(), now(), null, '2026-06-13T10:00:00Z', true);
            """);
    }

    private static PgConnectionFactory CreateFactory()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                    { ["ConnectionStrings:Postgres"] = PostgresIntegrationFixture.ConnectionString })
            .Build();
        return new PgConnectionFactory(config);
    }

    private static VarPriceDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<VarPriceDbContext>()
            .UseNpgsql(PostgresIntegrationFixture.ConnectionString)
            .Options;
        return new VarPriceDbContext(options);
    }

    private static DateTimeOffset Now(int day)
        => new(2026, 06, day, 10, 0, 0, TimeSpan.Zero);

    private static async Task ExecuteAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarLongAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(value);
    }

    private static async Task<bool> ScalarBoolAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return Convert.ToBoolean(value);
    }

    private static async Task<string?> ScalarStringAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private static async Task<DateTime> TimestampAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return DateTime.SpecifyKind(Convert.ToDateTime(value), DateTimeKind.Utc);
    }
}
