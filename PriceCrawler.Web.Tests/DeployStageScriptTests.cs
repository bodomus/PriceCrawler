using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PriceCrawler.Web.Tests;

public sealed class DeployStageScriptTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Script_DeclaresFailClosedStageDeploymentContract()
    {
        var script = File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), "Scripts", "deploy-stage.ps1"));

        Assert.Contains("Assert-ReleasePackage", script, StringComparison.Ordinal);
        Assert.Contains("Acquire-DeploymentLock", script, StringComparison.Ordinal);
        Assert.Contains("Stage pre-deployment backup", script, StringComparison.Ordinal);
        Assert.Contains("RefreshDatabaseFromDevelopment", script, StringComparison.Ordinal);
        Assert.Contains("downgrade is forbidden", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Stop-RecordedStageProcess", script, StringComparison.Ordinal);
        Assert.Contains("Wait-WebPort", script, StringComparison.Ordinal);
        Assert.Contains("Wait-WebHealth", script, StringComparison.Ordinal);
        Assert.Contains("DatabaseSchema__StartupMode", script, StringComparison.Ordinal);
        Assert.Contains("ValidateOnly", script, StringComparison.Ordinal);
        Assert.Contains("pricecrawler_stage_web", script, StringComparison.Ordinal);
        Assert.Contains("pricecrawler_stage_worker", script, StringComparison.Ordinal);
        Assert.Contains("databaseRollbackAutomatic = $false", script, StringComparison.Ordinal);
        Assert.Contains("productionTargetSupported = $false", script, StringComparison.Ordinal);
        Assert.DoesNotContain("[string]$Password", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Get-Process -Name", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ExternalStageConfigurationTemplates_UseSeparateRolesAndValidateOnly()
    {
        var root = ResolveRepositoryRoot();
        var web = File.ReadAllText(Path.Combine(root, "config", "stage-deployment", "web", "appsettings.Stage.example.json"));
        var worker = File.ReadAllText(Path.Combine(root, "config", "stage-deployment", "crawler", "appsettings.Stage.example.json"));

        Assert.Contains("pricecrawler_stage_web", web, StringComparison.Ordinal);
        Assert.DoesNotContain("pricecrawler_stage_worker", web, StringComparison.Ordinal);
        Assert.Contains("pricecrawler_stage_worker", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("pricecrawler_stage_web", worker, StringComparison.Ordinal);
        Assert.Contains("ValidateOnly", web, StringComparison.Ordinal);
        Assert.Contains("ValidateOnly", worker, StringComparison.Ordinal);
        Assert.Contains("<secret-from-external-store>", web, StringComparison.Ordinal);
        Assert.Contains("<secret-from-external-store>", worker, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task PackageValidation_ValidPackageSucceeds()
    {
        using var fixture = new TemporaryDirectory();
        var package = CreatePackage(fixture.Path);

        var result = await RunPowerShellAsync(
            ResolveRepositoryRoot(),
            "-PackagePath", package,
            "-ValidatePackageOnly");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Stage package validation succeeded", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task PackageValidation_ChecksumMismatchFails()
    {
        using var fixture = new TemporaryDirectory();
        var package = CreatePackage(fixture.Path);
        File.WriteAllText(package + ".sha256", new string('0', 64) + "  " + Path.GetFileName(package));

        var result = await RunPowerShellAsync(
            ResolveRepositoryRoot(),
            "-PackagePath", package,
            "-ValidatePackageOnly");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("does not match", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../escape.txt", "traversal")]
    [InlineData("web/.env", "secret")]
    [InlineData("graphify-out/graph.json", "graph")]
    [Trait("Category", "Unit")]
    public async Task PackageValidation_ForbiddenEntryFails(string forbiddenEntry, string expected)
    {
        using var fixture = new TemporaryDirectory();
        var package = CreatePackage(fixture.Path, forbiddenEntry);

        var result = await RunPowerShellAsync(
            ResolveRepositoryRoot(),
            "-PackagePath", package,
            "-ValidatePackageOnly");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(expected, result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task PackageValidation_WrongProductFails()
    {
        using var fixture = new TemporaryDirectory();
        var package = CreatePackage(fixture.Path, product: "AnotherProduct");

        var result = await RunPowerShellAsync(ResolveRepositoryRoot(), "-PackagePath", package, "-ValidatePackageOnly");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("product must be PriceCrawler", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task PackageValidation_SchemaMetadataMismatchFails()
    {
        using var fixture = new TemporaryDirectory();
        var package = CreatePackage(fixture.Path, targetSchemaVersion: 2);

        var result = await RunPowerShellAsync(ResolveRepositoryRoot(), "-PackagePath", package, "-ValidatePackageOnly");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("target does not match", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task WhatIf_ProductionLikeStageTargetFailsWithoutMutation()
    {
        using var fixture = new TemporaryDirectory();
        var package = CreatePackage(fixture.Path);
        var stageRoot = Path.Combine(fixture.Path, "forbidden-stage-target");

        var result = await RunPowerShellAsync(
            ResolveRepositoryRoot(),
            "-PackagePath", package,
            "-StageRoot", stageRoot,
            "-StageDatabase", "customer_prod",
            "-DevelopmentDatabase", "varprice",
            "-ProductionDatabase", "varprice_prod",
            "-DeployDatabaseUser", "pricecrawler_deploy",
            "-WebUrl", "http://127.0.0.1:18080",
            "-WorkerArguments", "vegetables",
            "-WhatIf");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Production-like Stage database name", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(stageRoot));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task WhatIf_WithValidReadOnlyPreflightDoesNotCreateStageRoot()
    {
        using var fixture = new TemporaryDirectory();
        var package = CreatePackage(fixture.Path);
        var stageRoot = Path.Combine(fixture.Path, "stage-target");
        var webConfig = Path.Combine(fixture.Path, "web.Stage.json");
        var workerConfig = Path.Combine(fixture.Path, "worker.Stage.json");
        WriteStageConfiguration(webConfig, "pricecrawler_stage_web");
        WriteStageConfiguration(workerConfig, "pricecrawler_stage_worker");
        var toolDirectory = CreateFakePostgresTools(fixture.Path);

        var result = await RunPowerShellWithPathAsync(
            ResolveRepositoryRoot(),
            toolDirectory,
            "-PackagePath", package,
            "-StageRoot", stageRoot,
            "-StageDatabase", "varprice_stage",
            "-DevelopmentDatabase", "varprice",
            "-ProductionDatabase", "varprice_prod",
            "-ToolMode", "Native",
            "-DeployDatabaseUser", "pricecrawler_deploy",
            "-WebUrl", "http://127.0.0.1:18080",
            "-WebConfigPath", webConfig,
            "-WorkerConfigPath", workerConfig,
            "-WorkerArguments", "vegetables",
            "-WhatIf");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Stage deployment dry run", result.Output, StringComparison.Ordinal);
        Assert.Contains("No backup, extraction, process, database, current, lock, log, or report mutation", result.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(stageRoot));
    }

    private static string CreatePackage(
        string directory,
        string? extraEntry = null,
        string product = "PriceCrawler",
        int targetSchemaVersion = 1)
    {
        var packagePath = Path.Combine(directory, "PriceCrawler-v1.2.3.zip");
        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "web/PriceCrawler.Web.dll", "web");
            WriteEntry(archive, "crawler/PriceCrawler.Worker.dll", "worker");
            WriteEntry(archive, "db/migrations/0001_baseline.sql", "select 1;");
            WriteEntry(archive, "db/scripts/provision-database-runtime-roles.ps1", "param([switch]$StageOnly, [int]$ExpectedSchemaVersion)");
            if (extraEntry is not null) WriteEntry(archive, extraEntry, "forbidden");
            var metadata = new
            {
                product,
                version = "v1.2.3",
                commit = "0123456789abcdef0123456789abcdef01234567",
                builtAtUtc = "2026-07-17T09:00:00Z",
                database = new
                {
                    minimumSchemaVersion = 1,
                    targetSchemaVersion,
                    migrations = new[] { "0001_baseline.sql" }
                },
                components = new { web = true, crawler = true, database = true }
            };
            WriteEntry(archive, "release.json", JsonSerializer.Serialize(metadata));
        }

        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packagePath))).ToLowerInvariant();
        File.WriteAllText(packagePath + ".sha256", $"{hash}  {Path.GetFileName(packagePath)}\n", new UTF8Encoding(false));
        return packagePath;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void WriteStageConfiguration(string path, string role)
    {
        var json = $$"""
                     {
                       "ConnectionStrings": {
                         "Postgres": "Host=localhost;Port=5432;Database=varprice_stage;Username={{role}};Password=${PRICECRAWLER_STAGE_DB_PASSWORD}"
                       },
                       "DatabaseSchema": {
                         "StartupMode": "ValidateOnly"
                       }
                     }
                     """;
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    private static string CreateFakePostgresTools(string root)
    {
        var tools = Path.Combine(root, "fake-tools");
        Directory.CreateDirectory(tools);
        File.WriteAllText(
            Path.Combine(tools, "psql.cmd"),
            "@echo off\r\necho %* | findstr /C:\"max(version)\" >nul\r\nif not errorlevel 1 (echo 1& exit /b 0)\r\necho t\r\nexit /b 0\r\n");
        foreach (var tool in new[] { "pg_dump", "pg_restore", "createdb", "dropdb" })
        {
            File.WriteAllText(Path.Combine(tools, tool + ".cmd"), "@echo off\r\nexit /b 0\r\n");
        }
        return tools;
    }

    private static async Task<ProcessResult> RunPowerShellAsync(
        string workingDirectory,
        params string[] arguments) =>
        await RunPowerShellAsync(workingDirectory, arguments, null);

    private static async Task<ProcessResult> RunPowerShellWithPathAsync(
        string workingDirectory,
        string environmentPathPrefix,
        params string[] arguments) =>
        await RunPowerShellAsync(workingDirectory, arguments, environmentPathPrefix);

    private static async Task<ProcessResult> RunPowerShellAsync(
        string workingDirectory,
        string[] arguments,
        string? environmentPathPrefix)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
        }
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(ResolveRepositoryRoot(), "Scripts", "deploy-stage.ps1"));
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        if (environmentPathPrefix is not null)
        {
            startInfo.Environment["PATH"] = environmentPathPrefix + Path.PathSeparator + startInfo.Environment["PATH"];
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, (await stdout) + (await stderr));
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PriceCrawler.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed record ProcessResult(int ExitCode, string Output);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pricecrawler-deploy-stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
