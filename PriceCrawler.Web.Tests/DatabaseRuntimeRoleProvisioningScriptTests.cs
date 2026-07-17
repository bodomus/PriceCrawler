using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

using Npgsql;

namespace PriceCrawler.Web.Tests;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class DatabaseRuntimeRoleProvisioningScriptTests
{
    private static readonly string RepositoryRoot = ResolveRepositoryRoot();
    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "scripts",
        "provision-database-runtime-roles.ps1");
    private static readonly string BaselinePath = Path.Combine(
        RepositoryRoot,
        "db",
        "migrations",
        "0001_baseline.sql");

    [Fact]
    [Trait("Category", "Unit")]
    public void Script_DeclaresFourNonDdlRolesAndExternalSecretInputs()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("pricecrawler_stage_web", script, StringComparison.Ordinal);
        Assert.Contains("pricecrawler_stage_worker", script, StringComparison.Ordinal);
        Assert.Contains("pricecrawler_prod_web", script, StringComparison.Ordinal);
        Assert.Contains("pricecrawler_prod_worker", script, StringComparison.Ordinal);
        Assert.Contains("PRICECRAWLER_STAGE_WEB_DB_PASSWORD", script, StringComparison.Ordinal);
        Assert.Contains("PRICECRAWLER_STAGE_WORKER_DB_PASSWORD", script, StringComparison.Ordinal);
        Assert.Contains("PRICECRAWLER_PROD_WEB_DB_PASSWORD", script, StringComparison.Ordinal);
        Assert.Contains("PRICECRAWLER_PROD_WORKER_DB_PASSWORD", script, StringComparison.Ordinal);
        Assert.Contains("nosuperuser nocreatedb nocreaterole", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("revoke create on schema public from public", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Removing inherited role memberships", script, StringComparison.Ordinal);
        Assert.DoesNotContain("grant select on all tables", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grant execute on all functions", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grant execute on all procedures", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grant execute on routines to", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE probe", script, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE probe", script, StringComparison.Ordinal);
        Assert.DoesNotContain("InitializeProduction", script, StringComparison.Ordinal);
        Assert.DoesNotContain("0001_baseline.sql", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DockerWorkflow_ProvisionsRolesStartsHostsAndDeniesDdl()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var stage = await TemporaryPostgresDatabase.CreateAsync("roles_stage");
        var production = await TemporaryPostgresDatabase.CreateAsync("roles_prod");
        var roleNames = new[]
        {
            $"pc_stage_web_{suffix}",
            $"pc_stage_worker_{suffix}",
            $"pc_prod_web_{suffix}",
            $"pc_prod_worker_{suffix}"
        };
        var passwords = Enumerable.Range(0, 4)
            .Select(index => $"Mpc81_{index}_{Guid.NewGuid():N}!")
            .ToArray();

        try
        {
            await stage.ExecuteFileAsync(BaselinePath);
            await production.ExecuteFileAsync(BaselinePath);
            var stageDatabase = new NpgsqlConnectionStringBuilder(stage.ConnectionString).Database!;
            var productionDatabase = new NpgsqlConnectionStringBuilder(production.ConnectionString).Database!;
            var container = Environment.GetEnvironmentVariable("PRICECRAWLER_POSTGRES_CONTAINER") ?? "var_postgres";
            var adminUser = new NpgsqlConnectionStringBuilder(PostgresIntegrationFixture.ConnectionString).Username!;

            var result = await RunProvisioningScriptAsync(
                new Dictionary<string, string?>
                {
                    ["TEST_STAGE_WEB_PASSWORD"] = passwords[0],
                    ["TEST_STAGE_WORKER_PASSWORD"] = passwords[1],
                    ["TEST_PROD_WEB_PASSWORD"] = passwords[2],
                    ["TEST_PROD_WORKER_PASSWORD"] = passwords[3]
                },
                "-ToolMode", "Docker",
                "-DockerContainer", container,
                "-AdminUser", adminUser,
                "-StageDatabase", stageDatabase,
                "-ProductionDatabase", productionDatabase,
                "-StageWebRole", roleNames[0],
                "-StageWorkerRole", roleNames[1],
                "-ProductionWebRole", roleNames[2],
                "-ProductionWorkerRole", roleNames[3],
                "-StageWebPasswordEnvironmentVariable", "TEST_STAGE_WEB_PASSWORD",
                "-StageWorkerPasswordEnvironmentVariable", "TEST_STAGE_WORKER_PASSWORD",
                "-ProductionWebPasswordEnvironmentVariable", "TEST_PROD_WEB_PASSWORD",
                "-ProductionWorkerPasswordEnvironmentVariable", "TEST_PROD_WORKER_PASSWORD");

            Assert.True(result.ExitCode == 0, result.CombinedOutput);
            Assert.DoesNotContain("Password=", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            foreach (var password in passwords)
                Assert.DoesNotContain(password, result.CombinedOutput, StringComparison.Ordinal);

            var stageWeb = RuntimeConnectionString(stage.ConnectionString, roleNames[0], passwords[0]);
            var stageWorker = RuntimeConnectionString(stage.ConnectionString, roleNames[1], passwords[1]);
            var productionWeb = RuntimeConnectionString(production.ConnectionString, roleNames[2], passwords[2]);
            var productionWorker = RuntimeConnectionString(production.ConnectionString, roleNames[3], passwords[3]);

            foreach (var connectionString in new[] { stageWeb, stageWorker, productionWeb, productionWorker })
            {
                Assert.Equal(1, await TemporaryPostgresDatabase.ScalarAsync<int>(
                    connectionString,
                    "select max(version) from schema_version"));
                await AssertDdlDeniedAsync(connectionString, "create table public.runtime_create_probe(id integer);");
                await AssertDdlDeniedAsync(connectionString, "alter table public.schema_version add column runtime_alter_probe integer;");
            }

            Assert.True(await TemporaryPostgresDatabase.ScalarAsync<long>(
                stageWeb,
                "select crawler_run_start('runtime-role-web-smoke')") > 0);
            Assert.True(await TemporaryPostgresDatabase.ScalarAsync<long>(
                productionWeb,
                "select crawler_run_start('runtime-role-web-smoke')") > 0);
            Assert.True(await TemporaryPostgresDatabase.ScalarAsync<bool>(
                stageWeb,
                "select has_table_privilege(current_user,'public.crawler_run','INSERT')"));
            Assert.False(await TemporaryPostgresDatabase.ScalarAsync<bool>(
                stageWeb,
                "select has_table_privilege(current_user,'public.product_catalog','INSERT')"));
            Assert.True(await TemporaryPostgresDatabase.ScalarAsync<bool>(
                stageWorker,
                "select has_table_privilege(current_user,'public.product_catalog','INSERT')"));
            Assert.False(await TemporaryPostgresDatabase.ScalarAsync<bool>(
                stageWeb,
                "select has_table_privilege(current_user,'public.product_catalog','SELECT')"));
            Assert.False(await TemporaryPostgresDatabase.ScalarAsync<bool>(
                stageWorker,
                "select has_table_privilege(current_user,'public.db_routine_script','SELECT')"));
            Assert.False(await TemporaryPostgresDatabase.ScalarAsync<bool>(
                stageWorker,
                "select has_table_privilege(current_user,'public.schema_version','UPDATE')"));
            await AssertDdlDeniedAsync(stageWeb, "select product_catalog_get_active_count('varus');");
            await AssertDdlDeniedAsync(productionWeb, "select product_catalog_get_active_count('varus');");

            await AssertWebStartsAsync("Stage", stageWeb);
            await AssertWorkerStartsAsync("Stage", stageWorker);
            await AssertWebStartsAsync("Production", productionWeb);
            await AssertWorkerStartsAsync("Production", productionWorker);
        }
        finally
        {
            await production.DisposeAsync();
            await stage.DisposeAsync();
            await DropRolesAsync(roleNames);
        }
    }

    private static async Task AssertDdlDeniedAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    private static async Task AssertWebStartsAsync(string environmentName, string connectionString)
    {
        var port = GetAvailableTcpPort();
        var process = StartHost(
            "PriceCrawler.Web",
            [],
            new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = environmentName,
                ["DOTNET_ENVIRONMENT"] = environmentName,
                ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}",
                ["ConnectionStrings__Postgres"] = connectionString,
                ["DatabaseSchema__StartupMode"] = "ValidateOnly"
            });

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!timeout.IsCancellationRequested && !process.HasExited && !await CanConnectAsync(port))
                await Task.Delay(100, timeout.Token);
            var connected = !process.HasExited && await CanConnectAsync(port);
            if (!connected)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                Assert.True(connected, await ReadOutputAsync(process));
            }
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            process.Dispose();
        }
    }

    private static async Task AssertWorkerStartsAsync(string environmentName, string connectionString)
    {
        using var process = StartHost(
            "PriceCrawler.Worker",
            ["collect-prices"],
            new Dictionary<string, string?>
            {
                ["DOTNET_ENVIRONMENT"] = environmentName,
                ["ConnectionStrings__Postgres"] = connectionString,
                ["DatabaseSchema__StartupMode"] = "ValidateOnly"
            });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await process.WaitForExitAsync(timeout.Token);
        var output = await ReadOutputAsync(process);
        Assert.Equal(0, process.ExitCode);
        Assert.Contains("Worker command started", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SchemaStartupMode=ValidateOnly", output, StringComparison.OrdinalIgnoreCase);
    }

    private static Process StartHost(
        string projectName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environment)
    {
        var assemblyPath = ResolveHostAssembly(projectName);
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(assemblyPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(assemblyPath);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        foreach (var pair in environment) startInfo.Environment[pair.Key] = pair.Value;
        return Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {projectName}.");
    }

    private static async Task<ProcessResult> RunProvisioningScriptAsync(
        IReadOnlyDictionary<string, string?> environment,
        params string[] arguments)
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
        foreach (var pair in environment) process.StartInfo.Environment[pair.Key] = pair.Value;

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private static string RuntimeConnectionString(string template, string role, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder(template)
        {
            Username = role,
            Password = password,
            Pooling = false
        };
        return builder.ConnectionString;
    }

    private static async Task DropRolesAsync(IEnumerable<string> roles)
    {
        var builder = new NpgsqlConnectionStringBuilder(PostgresIntegrationFixture.ConnectionString)
        {
            Database = "postgres"
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        foreach (var role in roles)
        {
            await using var command = new NpgsqlCommand($"drop role if exists {QuoteIdentifier(role)};", connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static int GetAvailableTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<bool> CanConnectAsync(int port)
    {
        using var client = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            return true;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    private static string ResolveHostAssembly(string projectName)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
                            ?? throw new DirectoryNotFoundException("Could not resolve test build configuration.");
        var path = Path.Combine(
            RepositoryRoot,
            projectName,
            "bin",
            configuration,
            "net8.0",
            $"{projectName}.dll");
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException($"Build {projectName} before running process integration tests.", path);
    }

    private static async Task<string> ReadOutputAsync(Process process)
        => await process.StandardOutput.ReadToEndAsync() + Environment.NewLine + await process.StandardError.ReadToEndAsync();

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
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => StandardOutput + Environment.NewLine + StandardError;
    }
}
