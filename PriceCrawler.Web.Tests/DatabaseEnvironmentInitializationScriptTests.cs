using System.Diagnostics;
using System.Text.Json;

using Npgsql;

namespace PriceCrawler.Web.Tests;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class DatabaseEnvironmentInitializationScriptTests
{
    private static readonly string RepositoryRoot = ResolveRepositoryRoot();
    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "scripts",
        "initialize-database-environments.ps1");
    private static readonly string BaselinePath = Path.Combine(
        RepositoryRoot,
        "db",
        "migrations",
        "0001_baseline.sql");

    [Fact]
    [Trait("Category", "Unit")]
    public void Script_DeclaresRequiredOperationsAndProductionGuards()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("SupportsShouldProcess = $true", script, StringComparison.Ordinal);
        Assert.Contains("$InitializeTest", script, StringComparison.Ordinal);
        Assert.Contains("$InitializeStage", script, StringComparison.Ordinal);
        Assert.Contains("$InitializeProduction", script, StringComparison.Ordinal);
        Assert.Contains("$InitializeAll", script, StringComparison.Ordinal);
        Assert.Contains("$ReplaceExistingTest", script, StringComparison.Ordinal);
        Assert.Contains("$ReplaceExistingStage", script, StringComparison.Ordinal);
        Assert.Contains("$ConfirmInitialProductionBootstrap", script, StringComparison.Ordinal);
        Assert.Contains("initial_bootstrap_completed=true", script, StringComparison.Ordinal);
        Assert.Contains("Future Production changes must use forward migrations", script, StringComparison.Ordinal);
        Assert.DoesNotContain("[switch]$Force", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[string]$Password", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Script_DuplicateNamesFailBeforeToolOrDatabaseAccess()
    {
        var result = await RunScriptAsync(
            "-AdminUser", "unused",
            "-InitializeTest",
            "-ToolMode", "Native",
            "-DevelopmentDatabase", "same_database",
            "-TestDatabase", "same_database",
            "-StageDatabase", "unique_stage",
            "-ProductionDatabase", "unique_prod");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must all be unique", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Script_ProductionRequiresConfirmationBeforeToolOrDatabaseAccess()
    {
        var result = await RunScriptAsync(
            "-AdminUser", "unused",
            "-InitializeProduction",
            "-ToolMode", "Native",
            "-DevelopmentDatabase", "unique_dev",
            "-TestDatabase", "unique_test",
            "-StageDatabase", "unique_stage",
            "-ProductionDatabase", "unique_prod");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("requires -ConfirmInitialProductionBootstrap", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Test", "varprice_test", "Ensure")]
    [InlineData("Stage", "varprice_stage", "ValidateOnly")]
    [InlineData("Staging", "varprice_stage", "ValidateOnly")]
    [InlineData("Production", "varprice_prod", "ValidateOnly")]
    [Trait("Category", "Unit")]
    public void WebAndWorkerEnvironmentTemplates_TargetOnlyTheirEnvironment(
        string environment,
        string expectedDatabase,
        string expectedMode)
    {
        foreach (var host in new[] { "PriceCrawler.Web", "PriceCrawler.Worker" })
        {
            var path = Path.Combine(RepositoryRoot, host, $"appsettings.{environment}.json");
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var connectionString = json.RootElement
                .GetProperty("ConnectionStrings")
                .GetProperty("Postgres")
                .GetString();
            var mode = json.RootElement
                .GetProperty("DatabaseSchema")
                .GetProperty("StartupMode")
                .GetString();

            Assert.NotNull(connectionString);
            Assert.Contains($"Database={expectedDatabase};", connectionString, StringComparison.Ordinal);
            Assert.Equal(expectedMode, mode);
            if (environment is "Stage" or "Staging")
                Assert.DoesNotContain("varprice_prod", connectionString, StringComparison.OrdinalIgnoreCase);
            if (environment == "Production")
                Assert.DoesNotContain("varprice_stage", connectionString, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DockerWorkflow_InitializesTestStageAndProductionAndRejectsSecondProductionBootstrap()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var source = $"pricecrawler_mpc81_dev_{suffix}";
        var test = $"pricecrawler_mpc81_test_{suffix}";
        var stage = $"pricecrawler_mpc81_stage_{suffix}";
        var production = $"pricecrawler_mpc81_prod_{suffix}";
        var artifacts = Path.Combine(Path.GetTempPath(), $"pricecrawler-mpc81-{suffix}");
        var report = Path.Combine(artifacts, "database-environments-bootstrap-report.md");
        var container = Environment.GetEnvironmentVariable("PRICECRAWLER_POSTGRES_CONTAINER") ?? "var_postgres";
        var template = new NpgsqlConnectionStringBuilder(PostgresIntegrationFixture.ConnectionString);
        var host = template.Host ?? throw new InvalidOperationException("PostgreSQL test host is not configured.");
        var adminUser = template.Username ?? throw new InvalidOperationException("PostgreSQL test user is not configured.");
        var admin = new NpgsqlConnectionStringBuilder(template.ConnectionString) { Database = "postgres" };

        try
        {
            await CreateDatabaseAsync(admin.ConnectionString, source);
            await CreateDatabaseAsync(admin.ConnectionString, test);
            await CreateDatabaseAsync(admin.ConnectionString, stage);
            await ExecuteFileAsync(ConnectionStringFor(template, source), BaselinePath);
            await ExecuteAsync(
                ConnectionStringFor(template, source),
                "insert into product(external_id,name,url) values('mpc81','snapshot row','https://example.test/mpc81');");

            var withoutStageReplacement = await RunScriptAsync(
                "-ToolMode", "Docker",
                "-DockerContainer", container,
                "-AdminUser", adminUser,
                "-InitializeStage",
                "-DevelopmentDatabase", source,
                "-TestDatabase", test,
                "-StageDatabase", stage,
                "-ProductionDatabase", production,
                "-ArtifactsRoot", artifacts,
                "-ReportPath", report);
            Assert.NotEqual(0, withoutStageReplacement.ExitCode);
            Assert.True(
                withoutStageReplacement.CombinedOutput.Contains("-ReplaceExistingStage", StringComparison.Ordinal),
                withoutStageReplacement.CombinedOutput);

            var result = await RunScriptAsync(
                "-ToolMode", "Docker",
                "-DockerContainer", container,
                "-HostName", host,
                "-Port", template.Port.ToString(),
                "-AdminUser", adminUser,
                "-InitializeAll",
                "-ReplaceExistingTest",
                "-ReplaceExistingStage",
                "-ConfirmInitialProductionBootstrap",
                "-DevelopmentDatabase", source,
                "-TestDatabase", test,
                "-StageDatabase", stage,
                "-ProductionDatabase", production,
                "-ArtifactsRoot", artifacts,
                "-ReportPath", report);

            Assert.True(result.ExitCode == 0, result.CombinedOutput);
            Assert.DoesNotContain("Password=", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, await ScalarAsync<int>(ConnectionStringFor(template, test), "select max(version) from schema_version"));
            Assert.Equal(1, await ScalarAsync<int>(ConnectionStringFor(template, stage), "select max(version) from schema_version"));
            Assert.Equal(1, await ScalarAsync<int>(ConnectionStringFor(template, production), "select max(version) from schema_version"));
            Assert.Equal(0, await ScalarAsync<int>(ConnectionStringFor(template, test), "select count(*) from product"));
            Assert.Equal(1, await ScalarAsync<int>(ConnectionStringFor(template, stage), "select count(*) from product"));
            Assert.Equal(1, await ScalarAsync<int>(ConnectionStringFor(template, production), "select count(*) from product"));

            var marker = await ScalarAsync<string>(
                admin.ConnectionString,
                $"select shobj_description(oid,'pg_database') from pg_database where datname='{production}'");
            Assert.Contains("initial_bootstrap_completed=true", marker, StringComparison.Ordinal);
            Assert.True(File.Exists(report));
            Assert.Contains(
                "After initial bootstrap, Production must never be replaced from Development.",
                await File.ReadAllTextAsync(report),
                StringComparison.Ordinal);
            Assert.NotEmpty(Directory.GetFiles(artifacts, "*.dump", SearchOption.AllDirectories));
            Assert.NotEmpty(Directory.GetFiles(artifacts, "*.log", SearchOption.AllDirectories));

            var secondProductionAttempt = await RunScriptAsync(
                "-ToolMode", "Docker",
                "-DockerContainer", container,
                "-AdminUser", adminUser,
                "-InitializeProduction",
                "-ConfirmInitialProductionBootstrap",
                "-DevelopmentDatabase", source,
                "-TestDatabase", test,
                "-StageDatabase", stage,
                "-ProductionDatabase", production,
                "-ArtifactsRoot", artifacts,
                "-ReportPath", report);

            Assert.NotEqual(0, secondProductionAttempt.ExitCode);
            Assert.Contains("permanently refused", secondProductionAttempt.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, await ScalarAsync<int>(ConnectionStringFor(template, production), "select count(*) from product"));
        }
        finally
        {
            foreach (var database in new[] { production, stage, test, source })
            {
                await DropDatabaseAsync(admin.ConnectionString, database);
            }

            if (Directory.Exists(artifacts)) Directory.Delete(artifacts, recursive: true);
        }
    }

    private static async Task<ProcessResult> RunScriptAsync(params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(ScriptPath);
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await process.WaitForExitAsync(timeout.Token);
        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    private static async Task CreateDatabaseAsync(string connectionString, string database)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"create database {QuoteIdentifier(database)};", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string connectionString, string database)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var terminate = new NpgsqlCommand(
                         "select pg_terminate_backend(pid) from pg_stat_activity where datname=@database and pid<>pg_backend_pid();",
                         connection))
        {
            terminate.Parameters.AddWithValue("database", database);
            await terminate.ExecuteNonQueryAsync();
        }

        await using var drop = new NpgsqlCommand($"drop database if exists {QuoteIdentifier(database)};", connection);
        await drop.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteFileAsync(string connectionString, string path)
        => await ExecuteAsync(connectionString, await File.ReadAllTextAsync(path));

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        var value = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(value!, typeof(T));
    }

    private static string ConnectionStringFor(NpgsqlConnectionStringBuilder template, string database)
    {
        var builder = new NpgsqlConnectionStringBuilder(template.ConnectionString)
        {
            Database = database,
            Pooling = false
        };
        return builder.ConnectionString;
    }

    private static string QuoteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

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

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => StandardOutput + Environment.NewLine + StandardError;
    }
}
