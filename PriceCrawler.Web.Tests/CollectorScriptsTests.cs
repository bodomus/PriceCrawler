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
    [InlineData("register-scheduled-task.ps1")]
    [InlineData("apply-collector-migrations.ps1")]
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

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScheduledRun_Scheduled_SkipsWhenTodayAlreadySucceeded()
    {
        using var fixture = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(fixture.Path, "state"));
        File.WriteAllText(
            Path.Combine(fixture.Path, "state", "last-scheduled-success.txt"),
            DateTime.Now.ToString("yyyy-MM-dd") + Environment.NewLine);

        var result = await RunPowerShellAsync(
            "-File", Path.Combine(ResolveRepositoryRoot(), "Scripts", "scheduled-run.ps1"),
            "-Scheduled",
            "-AppRoot", fixture.Path);

        Assert.True(result.ExitCode == 0, result.Output);
        var logs = Directory.GetFiles(Path.Combine(fixture.Path, "logs")).Select(file => Path.GetFileName(file)!).ToArray();
        Assert.Equal(new[] { "scheduled-run_skipped.log" }, logs);
        Assert.Contains("already completed today", File.ReadAllText(Path.Combine(fixture.Path, "logs", "scheduled-run_skipped.log")), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ScheduledRun_MarksSuccessOnlyForScheduledRunsWithSuccessfulWorker()
    {
        var script = File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), "Scripts", "scheduled-run.ps1"));

        Assert.Contains("if ($Scheduled -and $workerSucceeded)", script, StringComparison.Ordinal);
        Assert.Contains("$workerSucceeded = $false", script, StringComparison.Ordinal);
        Assert.True(
            script.IndexOf("already completed today", StringComparison.Ordinal) <
            script.IndexOf("Start-DockerEngine)", StringComparison.Ordinal),
            "A skipped scheduled start must exit before Docker is touched.");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RegisterScheduledTask_RetriesHourlyFromEightAndReplacesLegacyTask()
    {
        var root = ResolveRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "Scripts", "register-scheduled-task.ps1"));

        Assert.Contains("$StartTime = \"08:00\"", script, StringComparison.Ordinal);
        Assert.Contains("-StartWhenAvailable", script, StringComparison.Ordinal);
        Assert.Contains("-MultipleInstances IgnoreNew", script, StringComparison.Ordinal);
        Assert.Contains("-RepetitionInterval (New-TimeSpan -Hours 1)", script, StringComparison.Ordinal);
        Assert.Contains("scripts\\scheduled-run.ps1", script, StringComparison.Ordinal);
        Assert.Contains("-Scheduled -AppRoot", script, StringComparison.Ordinal);
        Assert.Contains("\"Magazine Crawler\"", script, StringComparison.Ordinal);

        var publish = File.ReadAllText(Path.Combine(root, "Scripts", "publish-collector.ps1"));
        Assert.Contains("@(\"scheduled-run.ps1\", \"backup-database.ps1\")", publish, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CollectorMigrations_BackUpBeforeApplyingAndPublishRefusesStaleSchema()
    {
        var root = ResolveRepositoryRoot();
        var migrate = File.ReadAllText(Path.Combine(root, "Scripts", "apply-collector-migrations.ps1"));
        var backupAt = migrate.IndexOf("backup-database.ps1", StringComparison.Ordinal);
        var applyAt = migrate.IndexOf("docker exec -i", StringComparison.Ordinal);
        Assert.True(backupAt > 0 && applyAt > backupAt, "The backup must run before any migration is applied.");
        Assert.Contains("ON_ERROR_STOP=1", migrate, StringComparison.Ordinal);
        Assert.Contains("ExpectedVersion", migrate, StringComparison.Ordinal);
        Assert.Contains("Read-Host", migrate, StringComparison.Ordinal);

        var publish = File.ReadAllText(Path.Combine(root, "Scripts", "publish-collector.ps1"));
        Assert.Contains("Assert-WorkingDatabaseSchema", publish, StringComparison.Ordinal);
        Assert.Contains("apply-collector-migrations.ps1", publish, StringComparison.Ordinal);
        Assert.Contains("[switch]$SkipSchemaCheck", publish, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PublishCollector_ChecksTheTargetFrameworkRuntimeBeforePublishing()
    {
        var root = ResolveRepositoryRoot();
        var publish = File.ReadAllText(Path.Combine(root, "Scripts", "publish-collector.ps1"));

        var checkAt = publish.IndexOf("\nAssert-DotNetRuntime", StringComparison.Ordinal);
        var publishAt = publish.IndexOf("dotnet publish $projectPath", StringComparison.Ordinal);
        Assert.True(checkAt > 0 && publishAt > checkAt, "The runtime check must run before dotnet publish.");
        Assert.Contains("--list-runtimes", publish, StringComparison.Ordinal);
        Assert.Contains("Microsoft.AspNetCore.App", publish, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScheduledRun_RunsWorkerFromFixedActiveFolderAndKeepsItsLogs()
    {
        using var fixture = new TemporaryDirectory();
        var worker = Path.Combine(fixture.Path, "worker");
        PublishFakeVersion(worker, "v1");
        Directory.CreateDirectory(Path.Combine(worker, "v1", "logs"));
        File.WriteAllText(Path.Combine(worker, "v1", "logs", "from-version-folder.log"), "old");
        File.WriteAllText(Path.Combine(worker, "current.txt"), "v1");

        var first = await RunCollectorActivationAsync(fixture.Path);

        var active = Path.Combine(worker, "active");
        Assert.True(first.ExitCode == 0, first.Output);
        Assert.Contains(Path.Combine(active, "PriceCrawler.Worker.exe"), first.Output, StringComparison.Ordinal);
        Assert.Contains("Activating collector v1", first.Output, StringComparison.Ordinal);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(active, "PriceCrawler.Worker.exe")));
        Assert.False(Directory.Exists(Path.Combine(active, "logs")), "Logs of the version folder must not be copied.");

        Directory.CreateDirectory(Path.Combine(active, "logs"));
        File.WriteAllText(Path.Combine(active, "logs", "pricecrawler-worker.log"), "run 1");
        var same = await RunCollectorActivationAsync(fixture.Path);
        Assert.True(same.ExitCode == 0, same.Output);
        Assert.DoesNotContain("Activating collector", same.Output, StringComparison.Ordinal);

        PublishFakeVersion(worker, "v2");
        File.WriteAllText(Path.Combine(worker, "current.txt"), "v2");
        var second = await RunCollectorActivationAsync(fixture.Path);

        Assert.True(second.ExitCode == 0, second.Output);
        Assert.Contains("Activating collector v2", second.Output, StringComparison.Ordinal);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(active, "PriceCrawler.Worker.exe")));
        Assert.Equal("run 1", File.ReadAllText(Path.Combine(active, "logs", "pricecrawler-worker.log")));
        Assert.False(Directory.Exists(Path.Combine(worker, "active.new")));
        Assert.True(File.Exists(Path.Combine(worker, "v1", "PriceCrawler.Worker.exe")), "Rollback needs the old version folder.");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PublishCollector_NeverCleansUpTheActiveFolder()
    {
        var publish = File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), "Scripts", "publish-collector.ps1"));

        Assert.Contains("$reserved = @(\"active\", \"active.new\")", publish, StringComparison.Ordinal);
        Assert.Contains("($reserved -notcontains $_.Name)", publish, StringComparison.Ordinal);
    }

    private static void PublishFakeVersion(string workerRoot, string version)
    {
        var path = Path.Combine(workerRoot, version);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "PriceCrawler.Worker.exe"), version);
        File.WriteAllText(Path.Combine(path, "collector-release.json"), $$"""{ "version": "{{version}}" }""");
    }

    /// <summary>Loads Write-Log, Sync-ActiveCollector and Get-CollectorExecutable from scheduled-run.ps1 and runs the last one.</summary>
    private static Task<ProcessResult> RunCollectorActivationAsync(string appRoot)
    {
        var path = Path.Combine(ResolveRepositoryRoot(), "Scripts", "scheduled-run.ps1");
        var command =
            "$ErrorActionPreference = 'Stop'; " +
            $"$AppRoot = '{appRoot}'; $logPath = Join-Path '{appRoot}' 'activation.log'; " +
            $"$ast = [System.Management.Automation.Language.Parser]::ParseFile('{path}', [ref]$null, [ref]$null); " +
            "$names = @('Write-Log', 'Sync-ActiveCollector', 'Get-CollectorExecutable'); " +
            "$ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $names -contains $node.Name }, $true) | " +
            "ForEach-Object { . ([scriptblock]::Create($_.Extent.Text)) }; " +
            "$exe = Get-CollectorExecutable; if ($null -eq $exe) { exit 20 }; Write-Output \"EXE=$exe\"";
        return RunPowerShellAsync("-Command", command);
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
