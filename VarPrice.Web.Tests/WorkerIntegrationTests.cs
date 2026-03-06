using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Npgsql;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;
using VarPrice.Application.UseCases;
using VarPrice.Infrastructure.Persistence;

namespace VarPrice.Web.Tests;

public sealed class WorkerIntegrationTests : IAsyncLifetime
{
    private readonly TestcontainersContainer _postgres = new TestcontainersBuilder<TestcontainersContainer>()
        .WithImage("postgres:16-alpine")
        .WithEnvironment("POSTGRES_DB", "varprice")
        .WithEnvironment("POSTGRES_USER", "varprice")
        .WithEnvironment("POSTGRES_PASSWORD", "varprice")
        .WithPortBinding(55432, 5432)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task RunCrawlerUseCase_PersistsRunAndSnapshots()
    {
        var connectionString = "Host=localhost;Port=55432;Database=varprice;Username=varprice;Password=varprice";

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = connectionString })
            .Build();

        var factory = new PgConnectionFactory(config);
        var schema = new SchemaBootstrapper(factory, NullLogger<SchemaBootstrapper>.Instance);
        await schema.EnsureSchemaAsync();

        var useCase = new RunCrawlerUseCase(
            Options.Create(new CrawlerOptions
                { SitemapIndexUrl = "unused", VegetablesUrlContains = "ovochi", MaxProductsPerRun = 1 }),
            Options.Create(new UrlFilterOptions()),
            new StaticSource(["https://varus.ua/kyiv/ovochi/item"]),
            new StaticExtractor(ProductExtractResult.Success(
                new ProductCard("sku1", "Name", "https://varus.ua/kyiv/ovochi/item", 12m, null, false, true, 1m, "kg",
                    "kyiv"),
                200,
                10,
                1.0d)),
            new PgCrawlerRunRepository(factory),
            new PgIngestionRunRepository(factory),
            new PgPriceSnapshotRepository(factory),
            NullLogger<RunCrawlerUseCase>.Instance);

        var result = await useCase.RunVegetablesAsync(CancellationToken.None);
        Assert.Equal("ok", result.Status);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        Assert.True(await ScalarAsync(conn, "select count(*) from crawler_run") >= 1);
        Assert.True(await ScalarAsync(conn, "select count(*) from ingestion_run") >= 1);
        Assert.True(await ScalarAsync(conn, "select count(*) from price_snapshot") >= 1);
        Assert.True(await ScalarAsync(conn, "select count(*) from ingestion_run where crawler_run_id is not null") >=
                    1);
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(value);
    }

    private sealed class StaticSource(IReadOnlyList<string> urls) : IProductUrlSource
    {
        public Task<IReadOnlyList<string>> GetProductUrlsAsync(string sitemapIndexUrl, CancellationToken ct) =>
            Task.FromResult(urls);
    }

    private sealed class StaticExtractor(ProductExtractResult result) : IProductCardExtractor
    {
        public Task<ProductExtractResult> ExtractAsync(string url, CancellationToken ct) => Task.FromResult(result);
    }
}
