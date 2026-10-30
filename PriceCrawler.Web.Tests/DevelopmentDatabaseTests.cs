using System.Diagnostics;

using Microsoft.Extensions.Configuration;

using PriceCrawler.Infrastructure.Persistence;

namespace PriceCrawler.Web.Tests;

/// <summary>MPC-90: development never points at the working database varprice.</summary>
public sealed class DevelopmentDatabaseTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("PriceCrawler.Web")]
    [InlineData("PriceCrawler.Worker")]
    public void BaseConnectionString_TargetsDevelopmentDatabase(string project)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(ResolveRepositoryRoot(), project, "appsettings.json"))
            .AddJsonFile(Path.Combine(ResolveRepositoryRoot(), project, "appsettings.Development.json"))
            .Build();

        var target = DatabaseTarget.Describe(configuration);

        Assert.Contains("Database=varprice_stage;", target, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DatabaseTarget_DescribesDatabaseWithoutPassword()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Port=55432;Database=varprice_stage;Username=var;Password=secret-value"
            })
            .Build();

        var target = DatabaseTarget.Describe(configuration);

        Assert.Equal("Host=localhost; Port=55432; Database=varprice_stage; Username=var", target);
        Assert.DoesNotContain("secret-value", target, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DestructiveDebugSeed_IsGuardedForDevelopmentDatabaseOnly()
    {
        var seed = File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), "db", "seeds", "001__local_debug_month.sql"));

        Assert.Contains("current_database() <> 'varprice_stage'", seed, StringComparison.Ordinal);
        Assert.DoesNotContain("current_database() <> 'varprice' ", seed, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("varprice")]
    [InlineData("varprice_prod")]
    [InlineData("varprice_test")]
    public async Task RefreshStage_RefusesAnyOtherTargetDatabase(string target)
    {
        var result = await RunPowerShellAsync(
            "-File", Path.Combine(ResolveRepositoryRoot(), "Scripts", "refresh-stage-from-backup.ps1"),
            "-TargetDatabase", target,
            "-BackupFile", Path.Combine(Path.GetTempPath(), "does-not-matter.backup"),
            "-Force");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("TargetDatabase", result.Output, StringComparison.Ordinal);
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
}
