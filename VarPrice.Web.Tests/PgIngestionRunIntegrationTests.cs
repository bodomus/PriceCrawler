using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using System.Data;
using VarPrice.Web.Storage;

namespace VarPrice.Web.Tests;

public sealed class PgIngestionRunIntegrationTests
{
    private const string DefaultConnectionString = "Host=localhost;Port=55432;Database=varprice;Username=var;Password=myPassword";
    private static readonly Lazy<(bool IsAvailable, string? Error)> PostgresAvailability = new(ProbePostgres);

    [PgFact]
    public async Task EnsureSchemaAsync_CreatesIngestionForeignKeyAndIndexes()
    {
        await using var scope = await CreateSchemaScopeAsync();
        var factory = new TestPgConnectionFactory(scope.SchemaConnectionString);
        var bootstrapper = new SchemaBootstrapper(factory, NullLogger<SchemaBootstrapper>.Instance);

        await bootstrapper.EnsureSchemaAsync();

        await using var cn = new NpgsqlConnection(scope.SchemaConnectionString);
        await cn.OpenAsync();

        await using var fkCmd = cn.CreateCommand();
        fkCmd.CommandText = @"
select rt.relname
from pg_constraint c
join pg_class t on t.oid = c.conrelid
join pg_class rt on rt.oid = c.confrelid
join pg_namespace n on n.oid = t.relnamespace
where n.nspname = current_schema()
  and t.relname = 'price_snapshot'
  and c.conname = 'price_snapshot_run_id_fkey';";
        var referencedTable = (string?)await fkCmd.ExecuteScalarAsync();
        Assert.Equal("ingestion_run", referencedTable);

        await using var indexCmd = cn.CreateCommand();
        indexCmd.CommandText = @"
select indexname
from pg_indexes
where schemaname = current_schema()
  and indexname in ('ix_ingestion_run_crawler_run_id', 'ix_price_snapshot_run');";
        var indexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await indexCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes.Add(reader.GetString(0));
        }

        Assert.Contains("ix_ingestion_run_crawler_run_id", indexes);
        Assert.Contains("ix_price_snapshot_run", indexes);
    }

    [PgFact]
    public async Task InsertSnapshotAsync_WithIngestionRunId_DoesNotViolateForeignKey()
    {
        await using var scope = await CreateSchemaScopeAsync();
        var factory = new TestPgConnectionFactory(scope.SchemaConnectionString);
        var bootstrapper = new SchemaBootstrapper(factory, NullLogger<SchemaBootstrapper>.Instance);
        await bootstrapper.EnsureSchemaAsync();

        var crawlerRepo = new PgCrawlerRepository(factory);
        var ingestionRepo = new PgIngestionRunRepository(factory);
        var ct = CancellationToken.None;

        var crawlerRunId = crawlerRepo.StartRun("integration-test");
        await crawlerRepo.FinishRunAsync(crawlerRunId, "ok", "crawler finished", ct);

        var ingestionRunId = ingestionRepo.StartIngestion(crawlerRunId, "integration-test");
        var productKey = await crawlerRepo.UpsertProductAsync(
            "it-product-1",
            "IT Product 1",
            "https://example.test/products/it-product-1",
            null,
            null,
            ct);

        await crawlerRepo.InsertSnapshotAsync(ingestionRunId, productKey, "kyiv", 199.99m, 229.99m, true, true, ct);
        await ingestionRepo.FinishIngestionAsync(ingestionRunId, "ok", "ingestion finished", ct);

        await using var cn = new NpgsqlConnection(scope.SchemaConnectionString);
        await cn.OpenAsync(ct);

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = @"
select count(*)
from price_snapshot ps
join ingestion_run ir on ir.run_id = ps.run_id
where ps.run_id = @ingestionRunId
  and ir.crawler_run_id = @crawlerRunId;";
        cmd.Parameters.AddWithValue("ingestionRunId", ingestionRunId);
        cmd.Parameters.AddWithValue("crawlerRunId", crawlerRunId);

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        Assert.Equal(1, count);
    }

    private static string ResolveBaseConnectionString() =>
        Environment.GetEnvironmentVariable("VARPRICE_TEST_POSTGRES") ?? DefaultConnectionString;

    private static (bool IsAvailable, string? Error) ProbePostgres()
    {
        try
        {
            var csb = new NpgsqlConnectionStringBuilder(ResolveBaseConnectionString())
            {
                Timeout = 2,
                CommandTimeout = 2
            };

            using var cn = new NpgsqlConnection(csb.ConnectionString);
            cn.Open();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static async Task<TestSchemaScope> CreateSchemaScopeAsync()
    {
        var baseConnectionString = ResolveBaseConnectionString();
        var schemaName = $"it_{Guid.NewGuid():N}";

        await using (var cn = new NpgsqlConnection(baseConnectionString))
        {
            await cn.OpenAsync();
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = $"create schema {schemaName};";
            await cmd.ExecuteNonQueryAsync();
        }

        var csb = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            SearchPath = $"{schemaName},public"
        };

        return new TestSchemaScope(baseConnectionString, schemaName, csb.ConnectionString);
    }

    private sealed class PgFactAttribute : FactAttribute
    {
        public PgFactAttribute()
        {
            var availability = PostgresAvailability.Value;
            if (!availability.IsAvailable)
            {
                Skip = $"PostgreSQL is unavailable for integration tests. {availability.Error}";
            }
        }
    }

    private sealed class TestPgConnectionFactory(string connectionString) : IPgConnectionFactory
    {
        public IDbConnection Create() => new NpgsqlConnection(connectionString);
    }

    private sealed class TestSchemaScope(string baseConnectionString, string schemaName, string schemaConnectionString) : IAsyncDisposable
    {
        public string SchemaConnectionString { get; } = schemaConnectionString;

        public async ValueTask DisposeAsync()
        {
            await using var cn = new NpgsqlConnection(baseConnectionString);
            await cn.OpenAsync();
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = $"drop schema if exists {schemaName} cascade;";
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
