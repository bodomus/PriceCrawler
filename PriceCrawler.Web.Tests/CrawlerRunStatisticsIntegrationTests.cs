using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using PriceCrawler.Domain.Constants;
using PriceCrawler.Domain.Enums;
using PriceCrawler.Domain.Models;
using PriceCrawler.Infrastructure.Persistence;

namespace PriceCrawler.Web.Tests;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class CrawlerRunStatisticsIntegrationTests
{
    [Fact]
    public async Task Complete_PersistsCountersStagesAndReadModels()
    {
        await PrepareAsync();
        var executor = new PgRoutineExecutor(CreateFactory());
        var writer = new PgCrawlerRunRepository(executor);
        var reader = new PgCrawlerRunReadRepository(executor);
        var runId = await writer.StartAsync(CrawlerRunTypes.PriceCollection, "worker", null, default);
        var statistics = new CrawlerRunStatistics(SelectedCount: 3, EnqueuedCount: 3, SucceededCount: 2,
            DeadCount: 1, FailedCount: 1, ProductsCreatedCount: 1, ProductsUpdatedCount: 1,
            SnapshotsCreatedCount: 2, ErrorsCreatedCount: 1);

        await writer.CompleteAsync(runId, RunStatus.Error, statistics,
        [
            new CrawlerRunStageTiming(CrawlerRunStages.QueueProcessing, 25, 3),
            new CrawlerRunStageTiming(CrawlerRunStages.CatalogSelection, 5, 3),
            new CrawlerRunStageTiming(CrawlerRunStages.QueueEnqueue, 10, 3),
            new CrawlerRunStageTiming(CrawlerRunStages.RunFinalization, 7)
        ], "done", "dead", "one dead", default);

        var details = await reader.GetByIdAsync(runId, default);
        Assert.NotNull(details);
        Assert.Equal(CrawlerRunTypes.PriceCollection, details.RunType);
        Assert.Equal("worker", details.Source);
        Assert.Equal("error", details.Status);
        Assert.True(details.DurationMs >= 0);
        Assert.Equal(3, details.Statistics.SelectedCount);
        Assert.Equal(2, details.Statistics.SnapshotsCreatedCount);
        Assert.Equal("dead", details.ErrorCode);
        Assert.Equal("one dead", details.ErrorMessage);
        Assert.Equal("done", details.Note);
        Assert.Equal(
            [
                CrawlerRunStages.QueueProcessing,
                CrawlerRunStages.CatalogSelection,
                CrawlerRunStages.QueueEnqueue,
                CrawlerRunStages.RunFinalization
            ],
            details.StageTimings.Select(x => x.Stage));
        Assert.Equal(3, details.StageTimings[0].ItemCount);
        Assert.True(details.StageTimings[^1].DurationMs >= 7);

        var recent = await reader.GetRecentAsync(50, CrawlerRunTypes.PriceCollection, "error", default);
        Assert.Contains(recent, x => x.Id == runId && x.PrimaryCount == 3);
        var aggregate = await reader.GetAggregateAsync(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1),
            CrawlerRunTypes.PriceCollection, default);
        Assert.Equal(1, aggregate.TotalRuns);
        Assert.Equal(2, aggregate.TotalSnapshotsCreated);
    }

    [Fact]
    public async Task NegativeCounter_IsRejected()
    {
        await PrepareAsync();
        await using var connection = new NpgsqlConnection(PostgresIntegrationFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command =
            new NpgsqlCommand("insert into crawler_run(status, source, discovered_count) values ('ok','test',-1)",
                connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    private static async Task PrepareAsync()
    {
        var options = new DbContextOptionsBuilder<PriceCrawlerDbContext>()
            .UseNpgsql(PostgresIntegrationFixture.ConnectionString).Options;
        await using var context = new PriceCrawlerDbContext(options);
        await new SchemaBootstrapper(context, NullLogger<SchemaBootstrapper>.Instance).EnsureSchemaAsync();
        await using var connection = new NpgsqlConnection(PostgresIntegrationFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "truncate table crawl_error, price_snapshot, price_collect_queue, product_catalog, product, ingestion_run, crawler_run restart identity cascade",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static PgConnectionFactory CreateFactory() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = PostgresIntegrationFixture.ConnectionString }).Build());
}
