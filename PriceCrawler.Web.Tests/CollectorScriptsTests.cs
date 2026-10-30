using System.Diagnostics;

namespace PriceCrawler.Web.Tests;

public sealed class CollectorScriptsTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("publish-collector.ps1")]
    [InlineData("scheduled-run.ps1")]
    [InlineData("backup-database.ps1")]
    [InlineData("refresh-stage-from-backup.ps1")]
    public async Task Script_HasNoPowerShellSyntaxErrors(string scriptName)
    {
        var path = Path.Combine(ResolveRepositoryRoot(), "Scripts", scriptName);
        var command =
            "$errors = $null; " +
            $"[System.Management.Automation.Language.Parser]::ParseFile('{path}', [ref]$null, [ref]$errors) | Out-Null; " +
            "if ($errors.Count -gt 0) { $errors | ForEach-Object { $_.ToString() }; exit 1 }";

        var result = await RunPowerShellAsync("-Command", command);

        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ScheduledRun_UsesPublishedProductionWorkerAndBacksUpAfterIt()
    {
        var script = File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), "Scripts", "scheduled-run.ps1"));

        Assert.Contains("worker\\current.txt", script, StringComparison.Ordinal);
        Assert.Contains("DOTNET_ENVIRONMENT = \"Production\"", script, StringComparison.Ordinal);
        Assert.Contains("\"catalog-refresh\", \"collect-prices\"", script, StringComparison.Ordinal);
        Assert.Contains("backup-database.ps1", script, StringComparison.Ordinal);
        Assert.True(
            script.IndexOf("Invoke-Logged -FilePath $exePath", StringComparison.Ordinal) <
            script.IndexOf("Backup started.", StringComparison.Ordinal),
            "Backup must run after the Worker.");
        Assert.DoesNotContain("bin\\Debug", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VarPrice.Worker", script, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BackupScripts_AddressContainerByName()
    {
        var root = ResolveRepositoryRoot();
        foreach (var name in new[] { "backup-database.ps1", "backup_varus.bat", "restore_varus.bat" })
        {
            var script = File.ReadAllText(Path.Combine(root, "Scripts", name));
            Assert.DoesNotContain("b9c59ecb00ef", script, StringComparison.Ordinal);
        }

        var backup = File.ReadAllText(Path.Combine(root, "Scripts", "backup-database.ps1"));
        Assert.Contains("\"var_postgres\"", backup, StringComparison.Ordinal);
        Assert.Contains("pg_restore\", \"--list\"", backup, StringComparison.Ordinal);
        Assert.DoesNotContain("pause", backup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CollectorSettingsTemplate_TargetsWorkingDatabaseWithoutSecret()
    {
        var template = File.ReadAllText(Path.Combine(
            ResolveRepositoryRoot(), "config", "collector", "appsettings.Production.example.json"));

        Assert.Contains("Database=varprice;", template, StringComparison.Ordinal);
        Assert.Contains("ValidateOnly", template, StringComparison.Ordinal);
        Assert.Contains("<secret-from-local-file>", template, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BackupRotation_KeepsDailyWindowWeeklyNewestAndLegacyFiles()
    {
        using var fixture = new TemporaryDirectory();
        var now = new DateTime(2026, 10, 29, 6, 0, 0);
        string[] files =
        [
            "varus_2026-10-29_060000.backup", // today: keep
            "varus_2026-10-16_060000.backup", // last day of the 14-day window: keep
            "varus_2026-10-15_070000.backup", // week of 12.10, newest: keep
            "varus_2026-10-14_060000.backup", // week of 12.10, older: delete
            "varus_2026-10-13_060000.backup", // week of 12.10, older: delete
            "varus_2026-08-02_060000.backup", // week of 27.07, inside 13 weeks: keep
            "varus_2026-07-01_060000.backup", // older than 13 weeks: delete
            "varus_2026-03-15.backup",        // legacy name: never rotated
            "notes.txt"
        ];
        foreach (var file in files) File.WriteAllText(Path.Combine(fixture.Path, file), "x");

        var result = await RunPowerShellAsync(
            "-File", Path.Combine(ResolveRepositoryRoot(), "Scripts", "backup-database.ps1"),
            "-BackupDirectory", fixture.Path,
            "-RotateOnly",
            "-Now", now.ToString("s"));

        Assert.True(result.ExitCode == 0, result.Output);
        var remaining = Directory.GetFiles(fixture.Path).Select(file => Path.GetFileName(file)!).Order().ToArray();
        Assert.Equal(
            new[]
            {
                "notes.txt",
                "varus_2026-03-15.backup",
                "varus_2026-08-02_060000.backup",
                "varus_2026-10-15_070000.backup",
                "varus_2026-10-16_060000.backup",
                "varus_2026-10-29_060000.backup"
            },
            remaining);
    }

    private static async Task<ProcessResult> RunPowerShellAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh",
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
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pricecrawler-collector-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
