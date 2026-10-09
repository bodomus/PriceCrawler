using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace PriceCrawler.Web.Tests;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class DatabaseSchemaProcessGatingTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Web_StageValidationFailure_ExitsWithoutOpeningListeningPort()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        var port = GetAvailableTcpPort();

        var result = await RunHostAsync(
            "PriceCrawler.Web",
            [],
            new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Stage",
                ["DOTNET_ENVIRONMENT"] = "Stage",
                ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}",
                ["ConnectionStrings__Postgres"] = database.ConnectionString,
                ["DatabaseSchema__StartupMode"] = "ValidateOnly"
            });

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("schema_version table was not found", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Now listening on", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        Assert.False(await CanConnectAsync(port));
        Assert.False(await database.ScalarAsync<bool>("select to_regclass('public.schema_version') is not null"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Worker_StageValidationFailure_ExitsBeforeCreatingRunOrProcessingWork()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await database.ExecuteFileAsync(ResolveRepositoryFile("db", "migrations", "0001_baseline.sql"));
        await database.ExecuteAsync("update schema_version set version=0;");
        var before = await database.ScalarAsync<int>("select count(*) from crawler_run");

        var result = await RunHostAsync(
            "PriceCrawler.Worker",
            ["vegetables", "--once"],
            new Dictionary<string, string?>
            {
                ["DOTNET_ENVIRONMENT"] = "Stage",
                ["ConnectionStrings__Postgres"] = database.ConnectionString,
                ["DatabaseSchema__StartupMode"] = "ValidateOnly"
            });

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Actual schema version: 0", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Worker command started", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await database.ScalarAsync<int>("select count(*) from crawler_run"));
        Assert.Equal(0, await database.ScalarAsync<int>("select max(version) from schema_version"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Web_ProductionEnvironmentOverrideToEnsure_IsRejectedBeforeDatabaseAccess()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        var before = await database.ScalarAsync<int>(
            "select count(*) from information_schema.tables where table_schema='public'");
        var port = GetAvailableTcpPort();

        var result = await RunHostAsync(
            "PriceCrawler.Web",
            [],
            new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
                ["DOTNET_ENVIRONMENT"] = "Production",
                ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}",
                ["ConnectionStrings__Postgres"] = database.ConnectionString,
                ["DatabaseSchema__StartupMode"] = "Ensure"
            });

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Unsafe database schema startup configuration", result.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("Startup aborted before database schema mutation", result.CombinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        Assert.False(await CanConnectAsync(port));
        Assert.Equal(before, await database.ScalarAsync<int>(
            "select count(*) from information_schema.tables where table_schema='public'"));
    }

    private static async Task<ProcessResult> RunHostAsync(
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
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException($"Could not start {projectName}.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException($"{projectName} did not terminate after schema startup failure.");
        }

        var output = await standardOutput;
        var error = await standardError;
        return new ProcessResult(process.ExitCode, output, error);
    }

    private static string ResolveHostAssembly(string projectName)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
                            ?? throw new DirectoryNotFoundException("Could not resolve test build configuration.");
        var path = Path.Combine(
            ResolveRepositoryRoot(),
            projectName,
            "bin",
            configuration,
            "net8.0",
            $"{projectName}.dll");
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException($"Build {projectName} before running process integration tests.", path);
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

    private static string ResolveRepositoryFile(params string[] segments)
    {
        var path = Path.Combine([ResolveRepositoryRoot(), .. segments]);
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

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => StandardOutput + Environment.NewLine + StandardError;
    }
}
