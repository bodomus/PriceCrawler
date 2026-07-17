using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
        await using var database = await TemporaryPostgresDatabase.CreateAsync();

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
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await database.ExecuteAsync("create table unrelated(id integer primary key);");

        var error = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteFileAsync(BaselinePath));

        Assert.Contains("requires an empty public schema", error.MessageText, StringComparison.OrdinalIgnoreCase);
        Assert.False(await database.ScalarAsync<bool>("select to_regclass('public.crawler_run') is not null"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Bootstrap_ValidExistingDatabase_IsRepeatableAndPreservesApplicationRows()
    {
        await using var database = await CreateExistingDatabaseWithoutMetadataAsync();
        await database.ExecuteAsync(
            "insert into product(external_id,name,url) values('mpc80','preserved','https://example.test/mpc80');");
        var before = await database.ScalarAsync<int>("select count(*) from product");

        await database.ExecuteFileAsync(BootstrapPath);
        await database.ExecuteFileAsync(BootstrapPath);

        Assert.Equal(before, await database.ScalarAsync<int>("select count(*) from product"));
        Assert.Equal(1, await database.ScalarAsync<int>("select count(*) from schema_version"));
        Assert.Equal(1, await database.ScalarAsync<int>("select max(version) from schema_version"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Bootstrap_MissingRequiredTable_IsRejectedWithoutMetadataCreation()
    {
        await using var database = await CreateExistingDatabaseWithoutMetadataAsync();
        await database.ExecuteAsync("drop table crawl_error;");

        var error = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteFileAsync(BootstrapPath));

        Assert.Contains("crawl_error", error.MessageText, StringComparison.OrdinalIgnoreCase);
        Assert.False(await database.ScalarAsync<bool>("select to_regclass('public.schema_version') is not null"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Bootstrap_MissingRequiredColumn_IsRejectedWithoutMetadataCreation()
    {
        await using var database = await CreateExistingDatabaseWithoutMetadataAsync();
        await database.ExecuteAsync("alter table product rename column slug to unexpected_slug;");

        var error = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteFileAsync(BootstrapPath));

        Assert.Contains("product.slug", error.MessageText, StringComparison.OrdinalIgnoreCase);
        Assert.False(await database.ScalarAsync<bool>("select to_regclass('public.schema_version') is not null"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Bootstrap_IncompatibleCriticalColumnType_IsRejectedWithoutMetadataCreation()
    {
        await using var database = await CreateExistingDatabaseWithoutMetadataAsync();
        await database.ExecuteAsync("alter table product alter column external_id type text;");

        var error = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteFileAsync(BootstrapPath));

        Assert.Contains("product.external_id", error.MessageText, StringComparison.OrdinalIgnoreCase);
        Assert.False(await database.ScalarAsync<bool>("select to_regclass('public.schema_version') is not null"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Bootstrap_ConflictingVersionMetadata_IsRejectedWithoutRepair()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
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
    public async Task Ensure_EmptyDatabase_InitializesBaselineAndValidatesVersion(string environmentName)
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await using var context = CreateDbContext(database.ConnectionString);
        var coordinator = CreateCoordinator(context, DatabaseSchemaStartupMode.Ensure);

        await coordinator.ExecuteAsync(environmentName);

        Assert.Equal(1, await database.ScalarAsync<int>("select max(version) from schema_version"));
        Assert.True(await database.ScalarAsync<bool>("select to_regclass('public.product') is not null"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Ensure_TestDatabase_RepeatedExecutionIsDeterministicAndPreservesRows()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await using var context = CreateDbContext(database.ConnectionString);
        var coordinator = CreateCoordinator(context, DatabaseSchemaStartupMode.Ensure);
        await coordinator.ExecuteAsync("Test");
        await database.ExecuteAsync(
            "insert into product(external_id,name,url) values('repeat','preserved','https://example.test/repeat');");

        await coordinator.ExecuteAsync("Test");

        Assert.Equal(1, await database.ScalarAsync<int>("select max(version) from schema_version"));
        Assert.Equal(1, await database.ScalarAsync<int>("select count(*) from product where external_id='repeat'"));
    }

    [Theory]
    [InlineData("Stage")]
    [InlineData("Staging")]
    [InlineData("Production")]
    [Trait("Category", "Integration")]
    public async Task ValidateOnly_CompatibleSchema_SucceedsWithoutMutation(string environmentName)
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await database.ExecuteFileAsync(BaselinePath);
        await database.ExecuteAsync(
            "insert into product(external_id,name,url) values('readonly','preserved','https://example.test/readonly');");
        var before = await CaptureDatabaseStateAsync(database);
        await using var context = CreateDbContext(database.ConnectionString);
        var logger = new RecordingLogger<DatabaseSchemaStartupCoordinator>();
        var coordinator = CreateCoordinator(context, DatabaseSchemaStartupMode.ValidateOnly, logger);

        await coordinator.ExecuteAsync(environmentName);

        Assert.Equal(before, await CaptureDatabaseStateAsync(database));
        var success = Assert.Single(logger.Entries, entry => Equals(entry.GetValueOrDefault("Result"), "Succeeded"));
        Assert.Equal(environmentName, success["Environment"]);
        Assert.Equal(DatabaseSchemaStartupMode.ValidateOnly, success["SchemaStartupMode"]);
        Assert.Equal(DatabaseSchema.ExpectedVersion, success["ExpectedSchemaVersion"]);
        Assert.Equal(DatabaseSchema.ExpectedVersion, success["ActualSchemaVersion"]);
    }

    [Theory]
    [InlineData("Stage", 0)]
    [InlineData("Stage", 2)]
    [InlineData("Production", 0)]
    [InlineData("Production", 2)]
    [Trait("Category", "Integration")]
    public async Task ValidateOnly_VersionMismatchFailsWithoutRepair(string environmentName, int actualVersion)
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await database.ExecuteFileAsync(BaselinePath);
        await database.ExecuteAsync($"update schema_version set version={actualVersion};");
        var before = await CaptureDatabaseStateAsync(database);
        await using var context = CreateDbContext(database.ConnectionString);
        var coordinator = CreateCoordinator(context, DatabaseSchemaStartupMode.ValidateOnly);

        var error = await Assert.ThrowsAsync<DatabaseSchemaVersionMismatchException>(
            () => coordinator.ExecuteAsync(environmentName));

        Assert.Contains($"Actual schema version: {actualVersion}", error.Message, StringComparison.Ordinal);
        Assert.Contains("Automatic schema changes are disabled", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await CaptureDatabaseStateAsync(database));
    }

    [Theory]
    [InlineData("Stage")]
    [InlineData("Production")]
    [Trait("Category", "Integration")]
    public async Task ValidateOnly_MissingMetadataFailsWithoutCreatingAnySchema(string environmentName)
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await using var context = CreateDbContext(database.ConnectionString);
        var coordinator = CreateCoordinator(context, DatabaseSchemaStartupMode.ValidateOnly);

        var error = await Assert.ThrowsAsync<DatabaseSchemaVersionMismatchException>(
            () => coordinator.ExecuteAsync(environmentName));

        Assert.Contains("schema_version table was not found", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(await database.ScalarAsync<bool>("select to_regclass('public.crawler_run') is not null"));
        Assert.False(await database.ScalarAsync<bool>("select to_regclass('public.schema_version') is not null"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ValidateOnly_CurrentMetadataNeverRepairsMissingApplicationTables()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await database.ExecuteAsync("""
                                    create table schema_version
                                    (
                                        version integer not null primary key,
                                        migration_name varchar(200) not null,
                                        applied_at_utc timestamptz not null default now(),
                                        application_version varchar(50),
                                        checksum varchar(128)
                                    );
                                    insert into schema_version(version, migration_name, application_version)
                                    values (1, '0001_baseline', 'v0.4.1-alpha');
                                    """);
        await using var context = CreateDbContext(database.ConnectionString);
        var coordinator = CreateCoordinator(context, DatabaseSchemaStartupMode.ValidateOnly);

        await coordinator.ExecuteAsync("Stage");

        Assert.False(await database.ScalarAsync<bool>("select to_regclass('public.product') is not null"));
        Assert.False(await database.ScalarAsync<bool>("select to_regclass('public.crawler_run') is not null"));
        Assert.Equal(1, await database.ScalarAsync<int>("select count(*) from schema_version"));
    }

    [Theory]
    [InlineData("Stage")]
    [InlineData("Production")]
    [Trait("Category", "Integration")]
    public async Task UnsafeEnsure_IsRejectedBeforeDatabaseAccess(string environmentName)
    {
        await using var context = CreateDbContext(
            "Host=127.0.0.1;Port=1;Database=must_not_connect;Username=none;Timeout=1");
        var coordinator = CreateCoordinator(context, DatabaseSchemaStartupMode.Ensure);

        var error = await Assert.ThrowsAsync<DatabaseSchemaStartupConfigurationException>(
            () => coordinator.ExecuteAsync(environmentName));

        Assert.Equal(DatabaseSchemaStartupMode.Ensure, error.ConfiguredMode);
        Assert.Equal(DatabaseSchemaStartupMode.ValidateOnly, error.RequiredMode);
        Assert.Contains("Startup aborted before database schema mutation", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Host=", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProductionValidateOnly_SucceedsWithRoleThatHasNoDdlPermission()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await database.ExecuteFileAsync(BaselinePath);
        var runtimeConnectionString = await database.CreateReadOnlyRuntimeRoleAsync();
        Assert.False(await TemporaryPostgresDatabase.ScalarAsync<bool>(
            runtimeConnectionString,
            "select has_schema_privilege(current_user, 'public', 'CREATE')"));
        await using var context = CreateDbContext(runtimeConnectionString);
        var coordinator = CreateCoordinator(context, DatabaseSchemaStartupMode.ValidateOnly);

        await coordinator.ExecuteAsync("Production");

        Assert.Equal(1, await database.ScalarAsync<int>("select max(version) from schema_version"));
    }

    private static async Task<TemporaryPostgresDatabase> CreateExistingDatabaseWithoutMetadataAsync()
    {
        var database = await TemporaryPostgresDatabase.CreateAsync();
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

    private static async Task<string> CaptureDatabaseStateAsync(TemporaryPostgresDatabase database)
        => await database.ScalarAsync<string>("""
                                               select concat_ws('|',
                                                   (select count(*) from pg_catalog.pg_class object
                                                    join pg_catalog.pg_namespace namespace on namespace.oid=object.relnamespace
                                                    where namespace.nspname='public'),
                                                   (select count(*) from pg_catalog.pg_proc routine
                                                    join pg_catalog.pg_namespace namespace on namespace.oid=routine.pronamespace
                                                    where namespace.nspname='public'),
                                                   (select count(*) from schema_version),
                                                   (select count(*) from product),
                                                   (select count(*) from crawler_run),
                                                   (select count(*) from price_collect_queue),
                                                   (select count(*) from db_routine_script));
                                               """);

    private static DatabaseSchemaStartupCoordinator CreateCoordinator(
        PriceCrawlerDbContext context,
        DatabaseSchemaStartupMode startupMode,
        ILogger<DatabaseSchemaStartupCoordinator>? logger = null)
    {
        var bootstrapper = new SchemaBootstrapper(context, NullLogger<SchemaBootstrapper>.Instance);
        var initializer = new DatabaseSchemaInitializer(
            context,
            bootstrapper,
            NullLogger<DatabaseSchemaInitializer>.Instance);
        var validator = new DatabaseSchemaValidator(new DatabaseSchemaVersionReader(context));
        return new DatabaseSchemaStartupCoordinator(
            initializer,
            validator,
            Options.Create(new DatabaseSchemaOptions { StartupMode = startupMode }),
            logger ?? NullLogger<DatabaseSchemaStartupCoordinator>.Instance);
    }

    private static PriceCrawlerDbContext CreateDbContext(string connectionString)
        => new(new DbContextOptionsBuilder<PriceCrawlerDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    private static string ResolveRepositoryFile(params string[] segments)
    {
        var root = ResolveRepositoryRoot();
        var path = Path.Combine([root, .. segments]);
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException($"Could not locate repository file {Path.Combine(segments)}.");
    }

    private static string ResolveRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PriceCrawler.sln"))) return directory.FullName;
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<Dictionary<string, object?>> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                Entries.Add(values.ToDictionary(pair => pair.Key, pair => pair.Value));
            }
        }
    }
}
