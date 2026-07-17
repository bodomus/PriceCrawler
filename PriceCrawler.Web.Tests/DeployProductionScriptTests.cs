using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PriceCrawler.Web.Tests;

public sealed class DeployProductionScriptTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Script_DeclaresFailClosedProductionContract()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "Scripts", "deploy-production.ps1"));

        Assert.Contains("Assert-StageApproval", script, StringComparison.Ordinal);
        Assert.Contains("ConfirmProductionDeployment", script, StringComparison.Ordinal);
        Assert.Contains("initial_bootstrap_completed=true", script, StringComparison.Ordinal);
        Assert.Contains("Production pre-deployment backup", script, StringComparison.Ordinal);
        Assert.Contains("ProductionOnly = $true", script, StringComparison.Ordinal);
        Assert.Contains("pricecrawler_prod_web", script, StringComparison.Ordinal);
        Assert.Contains("pricecrawler_prod_worker", script, StringComparison.Ordinal);
        Assert.Contains("DatabaseSchema__StartupMode", script, StringComparison.Ordinal);
        Assert.Contains("ValidateOnly", script, StringComparison.Ordinal);
        Assert.Contains("Stop-RecordedProductionProcess", script, StringComparison.Ordinal);
        Assert.Contains("Wait-WebPort", script, StringComparison.Ordinal);
        Assert.Contains("Wait-WebHealth", script, StringComparison.Ordinal);
        Assert.Contains("databaseRollbackAutomatic = $false", script, StringComparison.Ordinal);
        Assert.Contains("databaseCopySupported = $false", script, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshDatabaseFromDevelopment", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Restore-LogicalDump", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-TextCommand dropdb", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoke-TextCommand createdb", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[switch]$Force", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Get-Process -Name", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[string]$Password", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ExternalProductionTemplates_UseSeparateRolesAndValidateOnly()
    {
        var root = RepositoryRoot();
        var web = File.ReadAllText(Path.Combine(root, "config", "production-deployment", "web", "appsettings.Production.example.json"));
        var worker = File.ReadAllText(Path.Combine(root, "config", "production-deployment", "crawler", "appsettings.Production.example.json"));

        Assert.Contains("varprice_prod", web, StringComparison.Ordinal);
        Assert.Contains("pricecrawler_prod_web", web, StringComparison.Ordinal);
        Assert.DoesNotContain("pricecrawler_prod_worker", web, StringComparison.Ordinal);
        Assert.Contains("pricecrawler_prod_worker", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("pricecrawler_prod_web", worker, StringComparison.Ordinal);
        Assert.Contains("ValidateOnly", web, StringComparison.Ordinal);
        Assert.Contains("ValidateOnly", worker, StringComparison.Ordinal);
        Assert.Contains("<secret-from-external-store>", web, StringComparison.Ordinal);
        Assert.Contains("<secret-from-external-store>", worker, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task InputsValidation_MatchingSuccessfulStageReportSucceeds()
    {
        using var fixture = new TemporaryDirectory();
        var package = CreatePackage(fixture.Path);
        var report = CreateStageReport(fixture.Path, package);

        var result = await RunAsync("-PackagePath", package, "-StageVerificationReportPath", report, "-ValidateInputsOnly");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Production inputs validation succeeded", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("result", "Failed", "result=Success")]
    [InlineData("version", "v9.9.9", "version")]
    [InlineData("commit", "ffffffffffffffffffffffffffffffffffffffff", "commit")]
    [InlineData("packageSha256", "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", "SHA-256")]
    [Trait("Category", "Unit")]
    public async Task InputsValidation_StageMismatchFails(string property, string value, string expected)
    {
        using var fixture = new TemporaryDirectory();
        var package = CreatePackage(fixture.Path);
        var report = CreateStageReport(fixture.Path, package, property, value);

        var result = await RunAsync("-PackagePath", package, "-StageVerificationReportPath", report, "-ValidateInputsOnly");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(expected, result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RealDeployment_RequiresExplicitConfirmationBeforeTargetAccess()
    {
        using var fixture = new TemporaryDirectory();
        var package = CreatePackage(fixture.Path);
        var report = CreateStageReport(fixture.Path, package);

        var result = await RunAsync(
            "-PackagePath", package,
            "-StageVerificationReportPath", report,
            "-ProductionRoot", Path.Combine(fixture.Path, "production"),
            "-WebUrl", "http://127.0.0.1:18081");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("requires -ConfirmProductionDeployment", result.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(fixture.Path, "production")));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task WhatIf_ValidProductionPreflightIsNonMutating()
    {
        using var fixture = new TemporaryDirectory();
        var package = CreatePackage(fixture.Path);
        var report = CreateStageReport(fixture.Path, package);
        var webConfig = WriteProductionConfig(fixture.Path, "web", "pricecrawler_prod_web");
        var workerConfig = WriteProductionConfig(fixture.Path, "worker", "pricecrawler_prod_worker");
        var productionRoot = Path.Combine(fixture.Path, "production");
        var tools = CreateFakePostgresTools(fixture.Path, includeMarker: true);

        var result = await RunWithPathAsync(
            tools,
            "-PackagePath", package,
            "-StageVerificationReportPath", report,
            "-ProductionRoot", productionRoot,
            "-ProductionDatabase", "varprice_prod",
            "-ToolMode", "Native",
            "-DeployDatabaseUser", "pricecrawler_deploy",
            "-WebUrl", "http://127.0.0.1:18081",
            "-WebConfigPath", webConfig,
            "-WorkerConfigPath", workerConfig,
            "-WorkerArguments", "vegetables",
            "-ConfirmProductionDeployment",
            "-WhatIf");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Production deployment dry run", result.Output, StringComparison.Ordinal);
        Assert.Contains("Stage approval:", result.Output, StringComparison.Ordinal);
        Assert.Contains("No backup, extraction, process, database, current, lock, log, or report mutation", result.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(productionRoot));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task WhatIf_MissingProductionIndependenceMarkerFailsWithoutMutation()
    {
        using var fixture = new TemporaryDirectory();
        var package = CreatePackage(fixture.Path);
        var report = CreateStageReport(fixture.Path, package);
        var webConfig = WriteProductionConfig(fixture.Path, "web", "pricecrawler_prod_web");
        var workerConfig = WriteProductionConfig(fixture.Path, "worker", "pricecrawler_prod_worker");
        var productionRoot = Path.Combine(fixture.Path, "production");
        var tools = CreateFakePostgresTools(fixture.Path, includeMarker: false);

        var result = await RunWithPathAsync(
            tools,
            "-PackagePath", package,
            "-StageVerificationReportPath", report,
            "-ProductionRoot", productionRoot,
            "-ToolMode", "Native",
            "-DeployDatabaseUser", "pricecrawler_deploy",
            "-WebUrl", "http://127.0.0.1:18081",
            "-WebConfigPath", webConfig,
            "-WorkerConfigPath", workerConfig,
            "-WorkerArguments", "vegetables",
            "-WhatIf");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("no completed independence marker", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(productionRoot));
    }

    private static string CreatePackage(string directory)
    {
        var path = Path.Combine(directory, "PriceCrawler-v1.2.3.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "web/PriceCrawler.Web.dll", "web");
            WriteEntry(archive, "crawler/PriceCrawler.Worker.dll", "worker");
            WriteEntry(archive, "db/migrations/0001_baseline.sql", "select 1;");
            WriteEntry(archive, "db/scripts/provision-database-runtime-roles.ps1", "param([switch]$ProductionOnly, [int]$ExpectedSchemaVersion)");
            WriteEntry(archive, "release.json", JsonSerializer.Serialize(new
            {
                product = "PriceCrawler",
                version = "v1.2.3",
                commit = "0123456789abcdef0123456789abcdef01234567",
                builtAtUtc = "2026-07-17T09:00:00Z",
                database = new { minimumSchemaVersion = 1, targetSchemaVersion = 1, migrations = new[] { "0001_baseline.sql" } },
                components = new { web = true, crawler = true, database = true }
            }));
        }
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        File.WriteAllText(path + ".sha256", $"{hash}  {Path.GetFileName(path)}\n", new UTF8Encoding(false));
        return path;
    }

    private static string CreateStageReport(string directory, string package, string? overrideProperty = null, string? overrideValue = null)
    {
        var values = new Dictionary<string, object?>
        {
            ["environment"] = "Stage",
            ["result"] = "Success",
            ["finishedAtUtc"] = "2026-07-17T10:00:00+00:00",
            ["version"] = "v1.2.3",
            ["commit"] = "0123456789abcdef0123456789abcdef01234567",
            ["packageSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package))).ToLowerInvariant(),
            ["database"] = new { afterSchemaVersion = 1 },
            ["web"] = new { portReady = true, healthStatus = "Healthy" },
            ["worker"] = new { started = true }
        };
        if (overrideProperty is not null) values[overrideProperty] = overrideValue;
        var path = Path.Combine(directory, "deploy-stage-report.json");
        File.WriteAllText(path, JsonSerializer.Serialize(values), new UTF8Encoding(false));
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string WriteProductionConfig(string directory, string component, string role)
    {
        var path = Path.Combine(directory, $"{component}.Production.json");
        File.WriteAllText(path, $$"""
            {
              "ConnectionStrings": {
                "Postgres": "Host=localhost;Port=5432;Database=varprice_prod;Username={{role}};Password=${PRICECRAWLER_PROD_DB_PASSWORD}"
              },
              "DatabaseSchema": {
                "StartupMode": "ValidateOnly"
              }
            }
            """, new UTF8Encoding(false));
        return path;
    }

    private static string CreateFakePostgresTools(string directory, bool includeMarker)
    {
        var tools = Path.Combine(directory, "fake-tools");
        Directory.CreateDirectory(tools);
        var marker = includeMarker ? "PriceCrawler; environment=Production; initial_bootstrap_completed=true" : string.Empty;
        File.WriteAllText(Path.Combine(tools, "psql.cmd"), $"""
            @echo off
            echo %* | findstr /C:"shobj_description" >nul
            if not errorlevel 1 (echo {marker}& exit /b 0)
            echo %* | findstr /C:"from pg_roles" >nul
            if not errorlevel 1 (
              echo pricecrawler_deploy^|f^|f^|f
              echo pricecrawler_prod_web^|f^|f^|f
              echo pricecrawler_prod_worker^|f^|f^|f
              exit /b 0
            )
            echo %* | findstr /C:"max(version)" >nul
            if not errorlevel 1 (echo 1& exit /b 0)
            echo t
            exit /b 0
            """, new UTF8Encoding(false));
        foreach (var tool in new[] { "pg_dump", "pg_restore" })
        {
            File.WriteAllText(Path.Combine(tools, tool + ".cmd"), "@echo off\r\nexit /b 0\r\n", new UTF8Encoding(false));
        }
        return tools;
    }

    private static Task<ProcessResult> RunAsync(params string[] arguments) => RunCoreAsync(arguments, null);

    private static Task<ProcessResult> RunWithPathAsync(string pathPrefix, params string[] arguments) => RunCoreAsync(arguments, pathPrefix);

    private static async Task<ProcessResult> RunCoreAsync(string[] arguments, string? pathPrefix)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh",
            WorkingDirectory = RepositoryRoot(),
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
        startInfo.ArgumentList.Add(Path.Combine(RepositoryRoot(), "Scripts", "deploy-production.ps1"));
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        if (pathPrefix is not null) startInfo.Environment["PATH"] = pathPrefix + Path.PathSeparator + startInfo.Environment["PATH"];
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, (await stdout) + (await stderr));
    }

    private static string RepositoryRoot()
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pricecrawler-deploy-production-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
