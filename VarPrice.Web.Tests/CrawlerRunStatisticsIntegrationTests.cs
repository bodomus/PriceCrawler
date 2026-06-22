using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using VarPrice.Domain.Constants;
using VarPrice.Domain.Enums;
using VarPrice.Domain.Models;
using VarPrice.Infrastructure.Persistence;

namespace VarPrice.Web.Tests;

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
            [new CrawlerRunStageTiming(CrawlerRunStages.QueueProcessing, 25, 3)], "done", "dead", "one dead", default);

        var details = await reader.GetByIdAsync(runId, default);
        Assert.NotNull(details);
        Assert.Equal(3, details.Statistics.SelectedCount);
        Assert.Equal(2, details.Statistics.SnapshotsCreatedCount);
        Assert.Single(details.StageTimings);
        Assert.Equal(3, details.StageTimings[0].ItemCount);

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
        var options = new DbContextOptionsBuilder<VarPriceDbContext>()
            .UseNpgsql(PostgresIntegrationFixture.ConnectionString).Options;
        await using var context = new VarPriceDbContext(options);
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
