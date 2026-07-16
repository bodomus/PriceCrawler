using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Npgsql;

using PriceCrawler.Infrastructure.Persistence;

namespace PriceCrawler.Web.Tests;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class DatabaseSchemaVersioningTests
{
    private static readonly string BaselinePath = ResolveRepositoryFile("db", "migrations", "0001_baseline.sql");
    private static readonly string BootstrapPath = ResolveRepositoryFile("db", "scripts", "bootstrap-schema-version.sql");

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Baseline_EmptyDatabase_CreatesCompleteVersionOneSchema()
    {
        await using var database = await TemporaryDatabase.CreateAsync();

        await database.ExecuteFileAsync(BaselinePath);

        Assert.Equal(1, await database.ScalarAsync<int>("select max(version) from schema_version"));
        Assert.Equal("0001_baseline", await database.ScalarAsync<string>(
            "select migration_name from schema_version where version = 1"));
        Assert.Equal("v0.4.1-alpha", await database.ScalarAsync<string>(
            "select application_version from schema_version where version = 1"));
        Assert.Equal(11, await database.ScalarAsync<int>(
            "select count(*) from information_schema.tables where table_schema='public' and table_type='BASE TABLE'"));
        Assert.Equal(6, await database.ScalarAsync<int>("select count(*) from db_routine_script"));
        Assert.True(await database.ScalarAsync<bool>(
            "select to_regprocedure('public.product_catalog_get_due(integer,timestamp with time zone,integer,text)') is not null"));

        await using var context = CreateDbContext(database.ConnectionString);
        var result = await new DatabaseSchemaVersionReader(context).ReadAsync();
        Assert.True(result.IsCompatible);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Baseline_NonEmptyDatabase_FailsWithoutPartialSchemaCreation()
    {
        await using var database = await TemporaryDatabase.CreateAsync();
        await database.ExecuteAsync("create table unrelated(id integer primary key);");

        var error = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteFileAsync(BaselinePath));

        Assert.Contains("requires an empty public schema", error.MessageText, StringComparison.OrdinalIgnoreCase);
        Assert.False(await database.ScalarAsync<bool>("select to_regclass('public.crawler_run') is not null"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Bootstrap_ValidExistingDatabase_IsRepeatableAndPreservesApplicationRows()
    {
        await using var database = await CreateExistingDatabaseAsync();
        await database.ExecuteAsync(
            "insert into product(external_id,name,url) values('mpc79','preserved','https://example.test/mpc79');");
        var before = await database.ScalarAsync<int>("select count(*) from product");

        await database.ExecuteFileAsync(BootstrapPath);
        await database.ExecuteFileAsync(BootstrapPath);

        Assert.Equal(before, await database.ScalarAsync<int>("select count(*) from product"));
        Assert.Equal(1, await database.ScalarAsync<int>("select count(*) from schema_version"));
        Assert.Equal(1, await database.ScalarAsync<int>("select max(version) from schema_version"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Bootstrap_MissingRequiredTable_IsRejected()
    {
        await using var database = await CreateExistingDatabaseAsync();
        await database.ExecuteAsync("drop table crawl_error;");

        var error = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteFileAsync(BootstrapPath));

        Assert.Contains("crawl_error", error.MessageText, StringComparison.OrdinalIgnoreCase);
        Assert.False(await database.ScalarAsync<bool>("select to_regclass('public.schema_version') is not null"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Bootstrap_MissingRequiredColumn_IsRejected()
    {
        await using var database = await CreateExistingDatabaseAsync();
        await database.ExecuteAsync("alter table product rename column slug to unexpected_slug;");

        var error = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteFileAsync(BootstrapPath));

        Assert.Contains("product.slug", error.MessageText, StringComparison.OrdinalIgnoreCase);
        Assert.False(await database.ScalarAsync<bool>("select to_regclass('public.schema_version') is not null"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Bootstrap_IncompatibleCriticalColumnType_IsRejected()
    {
        await using var database = await CreateExistingDatabaseAsync();
        await database.ExecuteAsync("alter table product alter column external_id type text;");

        var error = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteFileAsync(BootstrapPath));

        Assert.Contains("product.external_id", error.MessageText, StringComparison.OrdinalIgnoreCase);
        Assert.False(await database.ScalarAsync<bool>("select to_regclass('public.schema_version') is not null"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Bootstrap_ConflictingVersionMetadata_IsRejected()
    {
        await using var database = await TemporaryDatabase.CreateAsync();
        await database.ExecuteFileAsync(BaselinePath);
        await database.ExecuteAsync("update schema_version set migration_name='conflicting';");

        var error = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteFileAsync(BootstrapPath));

        Assert.Contains("conflicts", error.MessageText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("conflicting", await database.ScalarAsync<string>("select migration_name from schema_version"));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    [Trait("Category", "Integration")]
    public async Task Startup_CompatibleSchema_Succeeds(string environmentName)
    {
        await using var database = await TemporaryDatabase.CreateAsync();
        await database.ExecuteFileAsync(BaselinePath);
        await using var context = CreateDbContext(database.ConnectionString);
        var service = CreateStartupService(context, allowAutomaticInitialization: false);

        await service.ValidateAndInitializeAsync(environmentName);
    }

    [Theory]
    [InlineData("Stage", 0)]
    [InlineData("Staging", 2)]
    [InlineData("Production", 0)]
    [InlineData("Production", 2)]
    [Trait("Category", "Integration")]
    public async Task Startup_ProtectedEnvironment_VersionMismatchFails(string environmentName, int actualVersion)
    {
        await using var database = await TemporaryDatabase.CreateAsync();
        await database.ExecuteFileAsync(BaselinePath);
        await database.ExecuteAsync($"update schema_version set version={actualVersion};");
        await using var context = CreateDbContext(database.ConnectionString);
        var service = CreateStartupService(context, allowAutomaticInitialization: true);

        var error = await Assert.ThrowsAsync<DatabaseSchemaVersionMismatchException>(
            () => service.ValidateAndInitializeAsync(environmentName));

        Assert.Contains($"Actual: {actualVersion}", error.Message, StringComparison.Ordinal);
        Assert.Contains("Automatic schema changes are disabled", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Stage")]
    [InlineData("Production")]
    [Trait("Category", "Integration")]
    public async Task Startup_ProtectedEnvironment_MissingMetadataDoesNotCreateSchema(string environmentName)
    {
        await using var database = await TemporaryDatabase.CreateAsync();
        await using var context = CreateDbContext(database.ConnectionString);
        var service = CreateStartupService(context, allowAutomaticInitialization: true);

        var error = await Assert.ThrowsAsync<DatabaseSchemaVersionMismatchException>(
            () => service.ValidateAndInitializeAsync(environmentName));

        Assert.Contains("was not found", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(await database.ScalarAsync<bool>("select to_regclass('public.crawler_run') is not null"));
        Assert.False(await database.ScalarAsync<bool>("select to_regclass('public.schema_version') is not null"));
    }

    private static async Task<TemporaryDatabase> CreateExistingDatabaseAsync()
    {
        var database = await TemporaryDatabase.CreateAsync();
        try
        {
            await database.ExecuteFileAsync(BaselinePath);
            await database.ExecuteAsync("drop table schema_version;");
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    private static DatabaseSchemaStartupService CreateStartupService(
        PriceCrawlerDbContext context,
        bool allowAutomaticInitialization)
        => new(
            new SchemaBootstrapper(context, NullLogger<SchemaBootstrapper>.Instance),
            new DatabaseSchemaVersionReader(context),
            Options.Create(new DatabaseSchemaOptions
            {
                AllowAutomaticInitialization = allowAutomaticInitialization,
                ValidateOnStartup = true
            }),
            NullLogger<DatabaseSchemaStartupService>.Instance);

    private static PriceCrawlerDbContext CreateDbContext(string connectionString)
        => new(new DbContextOptionsBuilder<PriceCrawlerDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    private static string ResolveRepositoryFile(params string[] segments)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var solutionPath = Path.Combine(directory.FullName, "PriceCrawler.sln");
                if (File.Exists(solutionPath))
                {
                    var path = Path.Combine([directory.FullName, .. segments]);
                    if (File.Exists(path)) return path;
                }

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException($"Could not locate repository file {Path.Combine(segments)}.");
    }

    private sealed class TemporaryDatabase : IAsyncDisposable
    {
        private readonly string _databaseName;
        private bool _disposed;

        private TemporaryDatabase(string databaseName, string connectionString)
        {
            _databaseName = databaseName;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        public static async Task<TemporaryDatabase> CreateAsync()
        {
            var template = new NpgsqlConnectionStringBuilder(PostgresIntegrationFixture.ConnectionString);
            var databaseName = $"pricecrawler_mpc79_test_{Guid.NewGuid():N}";
            var admin = new NpgsqlConnectionStringBuilder(template.ConnectionString) { Database = "postgres" };
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"create database {QuoteIdentifier(databaseName)};", connection);
            await command.ExecuteNonQueryAsync();

            template.Database = databaseName;
            return new TemporaryDatabase(databaseName, template.ConnectionString);
        }

        public async Task ExecuteFileAsync(string path)
            => await ExecuteAsync(await File.ReadAllTextAsync(path));

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 120 };
            await command.ExecuteNonQueryAsync();
        }

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
            var value = await command.ExecuteScalarAsync();
            return (T)Convert.ChangeType(value!, typeof(T));
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            var template = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres" };
            await using var connection = new NpgsqlConnection(template.ConnectionString);
            await connection.OpenAsync();
            await using (var terminateCommand = new NpgsqlCommand("""
                                                                  select pg_terminate_backend(pid)
                                                                  from pg_stat_activity
                                                                  where datname = @database_name
                                                                    and pid <> pg_backend_pid();
                                                                  """, connection))
            {
                terminateCommand.Parameters.AddWithValue("database_name", _databaseName);
                await terminateCommand.ExecuteNonQueryAsync();
            }

            await using var dropCommand = new NpgsqlCommand(
                $"drop database if exists {QuoteIdentifier(_databaseName)};",
                connection);
            await dropCommand.ExecuteNonQueryAsync();
        }

        private static string QuoteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
