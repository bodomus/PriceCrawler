using Microsoft.Extensions.Configuration;

using PriceCrawler.Domain.Categories;
using PriceCrawler.Infrastructure.Persistence;

namespace PriceCrawler.Web.Tests;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class CategoryRepositoryTests
{
    private static readonly DateTimeOffset FirstRead = new(2026, 11, 1, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondRead = FirstRead.AddDays(7);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SyncTree_FirstRead_InsertsTreeWithParents()
    {
        await using var database = await CreateDatabaseAsync();
        var sut = CreateRepository(database);

        var result = await sut.SyncTreeAsync(InitialTree(), 0, FirstRead, CancellationToken.None);

        Assert.Equal(new CategorySyncResult(4, 4, 0, 0, 0, 0), result);
        Assert.Equal("frukti-svizhi", await database.ScalarAsync<string>("""
            select p.external_id from category c join category p on p.id = c.parent_id where c.external_id = 'banani'
            """));
        Assert.Equal(4, await sut.GetActiveCountAsync("varus", CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SyncTree_SecondRead_RenamesMovesAndDeactivatesWithoutDeleting()
    {
        await using var database = await CreateDatabaseAsync();
        var sut = CreateRepository(database);
        await sut.SyncTreeAsync(InitialTree(), 0, FirstRead, CancellationToken.None);
        var bananaId = await database.ScalarAsync<long>("select id from category where external_id = 'banani'");

        var result = await sut.SyncTreeAsync(CategoryTreeSnapshot.Create("varus",
        [
            Node("frukti-ovochi-gorihi", null),
            Node("frukti-svizhi", "frukti-ovochi-gorihi", name: "Фрукти"),
            Node("tropichni-frukti", "frukti-ovochi-gorihi"),
            Node("banani", "tropichni-frukti")
        ]), 0, SecondRead, CancellationToken.None);

        Assert.Equal(new CategorySyncResult(4, 1, 1, 1, 0, 1), result);
        Assert.Equal(bananaId, await database.ScalarAsync<long>("select id from category where external_id = 'banani'"));
        Assert.Equal("tropichni-frukti", await database.ScalarAsync<string>("""
            select p.external_id from category c join category p on p.id = c.parent_id where c.external_id = 'banani'
            """));
        Assert.False(await database.ScalarAsync<bool>("select is_active from category where external_id = 'ovochi-svizhi'"));
        Assert.Equal(5, await database.ScalarAsync<int>("select count(*)::int from category"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SyncTree_ReturningCategory_IsReactivated()
    {
        await using var database = await CreateDatabaseAsync();
        var sut = CreateRepository(database);
        await sut.SyncTreeAsync(InitialTree(), 0, FirstRead, CancellationToken.None);
        await sut.SyncTreeAsync(CategoryTreeSnapshot.Create("varus", [Node("frukti-ovochi-gorihi", null)]), 0,
            SecondRead, CancellationToken.None);

        var result = await sut.SyncTreeAsync(InitialTree(), 0, SecondRead.AddDays(7), CancellationToken.None);

        Assert.Equal(3, result.ReactivatedCount);
        Assert.Equal(0, await database.ScalarAsync<int>(
            "select count(*)::int from category where not is_active or deactivated_at is not null"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SyncTree_OtherStore_IsUntouched()
    {
        await using var database = await CreateDatabaseAsync();
        var sut = CreateRepository(database);
        await sut.SyncTreeAsync(InitialTree(), 0, FirstRead, CancellationToken.None);

        await sut.SyncTreeAsync(CategoryTreeSnapshot.Create("silpo", [Node("frukti", null)]), 0, SecondRead,
            CancellationToken.None);

        Assert.Equal(4, await sut.GetActiveCountAsync("varus", CancellationToken.None));
        Assert.Equal(1, await sut.GetActiveCountAsync("silpo", CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LinkProducts_CompleteListing_ReplacesStaleLinksAndCountsUnknownUrls()
    {
        await using var database = await CreateDatabaseAsync();
        var sut = CreateRepository(database);
        await sut.SyncTreeAsync(InitialTree(), 0, FirstRead, CancellationToken.None);
        await InsertCatalogItemsAsync(database, "banan-1", "banan-2", "banan-3");

        await sut.LinkProductsAsync("varus",
            [new CategoryProductListing("banani", [Url("banan-1"), Url("banan-2")], IsComplete: true)],
            0, FirstRead, CancellationToken.None);
        var result = await sut.LinkProductsAsync("varus",
            [new CategoryProductListing("banani", [Url("banan-2"), Url("banan-3"), Url("unknown")], IsComplete: true)],
            0, SecondRead, CancellationToken.None);

        Assert.Equal(new CategoryLinkResult(2, 1, 1), result);
        Assert.Equal("banan-2,banan-3", await database.ScalarAsync<string>("""
            select string_agg(pc.slug, ',' order by pc.slug)
            from product_catalog_category pcc join product_catalog pc on pc.id = pcc.product_catalog_id
            """));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LinkProducts_IncompleteListing_KeepsExistingLinks()
    {
        await using var database = await CreateDatabaseAsync();
        var sut = CreateRepository(database);
        await sut.SyncTreeAsync(InitialTree(), 0, FirstRead, CancellationToken.None);
        await InsertCatalogItemsAsync(database, "banan-1", "banan-2");
        await sut.LinkProductsAsync("varus",
            [new CategoryProductListing("banani", [Url("banan-1"), Url("banan-2")], IsComplete: true)],
            0, FirstRead, CancellationToken.None);

        var result = await sut.LinkProductsAsync("varus",
            [new CategoryProductListing("banani", [Url("banan-2")], IsComplete: false)],
            0, SecondRead, CancellationToken.None);

        Assert.Equal(0, result.RemovedCount);
        Assert.Equal(2, await database.ScalarAsync<int>("select count(*)::int from product_catalog_category"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetChildren_ReturnsLevelWithDescendantProductCounts()
    {
        await using var database = await CreateDatabaseAsync();
        var sut = CreateRepository(database);
        await sut.SyncTreeAsync(InitialTree(), 0, FirstRead, CancellationToken.None);
        await InsertCatalogItemsAsync(database, "banan-1", "banan-2", "morkva-1");
        await sut.LinkProductsAsync("varus",
        [
            new CategoryProductListing("banani", [Url("banan-1"), Url("banan-2")], IsComplete: true),
            new CategoryProductListing("ovochi-svizhi", [Url("morkva-1"), Url("banan-1")], IsComplete: true)
        ], 0, FirstRead, CancellationToken.None);

        var roots = await sut.GetChildrenAsync("varus", null, CancellationToken.None);
        var root = Assert.Single(roots);
        var children = await sut.GetChildrenAsync("varus", root.Id, CancellationToken.None);

        Assert.True(root.HasChildren);
        Assert.Equal(3, root.ProductCount);
        Assert.Equal(["frukti-svizhi", "ovochi-svizhi"], children.Select(child => child.ExternalId));
        Assert.Equal([2, 2], children.Select(child => child.ProductCount));
        Assert.Equal([true, false], children.Select(child => child.HasChildren));
    }

    private static CategoryTreeSnapshot InitialTree() => CategoryTreeSnapshot.Create("varus",
    [
        Node("frukti-ovochi-gorihi", null, 0),
        Node("frukti-svizhi", "frukti-ovochi-gorihi", 0),
        Node("banani", "frukti-svizhi", 0),
        Node("ovochi-svizhi", "frukti-ovochi-gorihi", 1)
    ]);

    private static DiscoveredCategory Node(string key, string? parent, int order = 0, string? name = null)
        => new(key, name ?? key, $"https://varus.ua/{key}", parent, order);

    private static string Url(string slug) => $"https://varus.ua/{slug}";

    private static async Task InsertCatalogItemsAsync(TemporaryPostgresDatabase database, params string[] slugs)
    {
        foreach (var slug in slugs)
        {
            await database.ExecuteAsync($"""
                insert into product_catalog(source, url, normalized_url, slug, first_discovered_at, last_discovered_at)
                values ('varus', '{Url(slug)}', '{Url(slug)}', '{slug}', now(), now());
                """);
        }
    }

    private static PgCategoryRepository CreateRepository(TemporaryPostgresDatabase database)
        => new(new PgConnectionFactory(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = database.ConnectionString })
            .Build()));

    private static async Task<TemporaryPostgresDatabase> CreateDatabaseAsync()
    {
        var database = await TemporaryPostgresDatabase.CreateAsync("mpc96");
        try
        {
            var directory = Path.GetDirectoryName(SqlAssetLocator.ResolveBaselinePath())!;
            await database.ExecuteFileAsync(SqlAssetLocator.ResolveBaselinePath());
            foreach (var migration in DatabaseMigrationCatalog.LoadForwardMigrations(directory, DatabaseSchema.ExpectedVersion))
            {
                await database.ExecuteFileAsync(migration.FullPath);
            }

            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }
}
