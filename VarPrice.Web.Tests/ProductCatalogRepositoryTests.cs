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

    private static async Task<DateTime> TimestampAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return DateTime.SpecifyKind(Convert.ToDateTime(value), DateTimeKind.Utc);
    }
}
