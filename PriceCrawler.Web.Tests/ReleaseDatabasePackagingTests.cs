using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

using PriceCrawler.Infrastructure.Persistence;

namespace PriceCrawler.Web.Tests;

public sealed class ReleaseDatabasePackagingTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void DatabaseReleaseAssets_MatchApplicationSchemaContract()
    {
        var root = ResolveRepositoryRoot();
        var baselinePath = Path.Combine(root, "db", "migrations", "0001_baseline.sql");
        var bootstrapPath = Path.Combine(root, "db", "scripts", "bootstrap-schema-version.sql");

        Assert.True(File.Exists(baselinePath));
        Assert.True(File.Exists(bootstrapPath));
        Assert.Contains(
            $"VALUES ({DatabaseSchema.ExpectedVersion}, '0001_baseline'",
            File.ReadAllText(baselinePath),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            $"VALUES ({DatabaseSchema.ExpectedVersion}, '0001_baseline'",
            File.ReadAllText(bootstrapPath),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BuildReleaseScript_DeclaresDatabasePackageAndVersionMetadata()
    {
        var script = File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), "scripts", "build-release.ps1"));

        Assert.Contains("databaseMigrationsPath", script, StringComparison.Ordinal);
        Assert.Contains("databaseScriptsPath", script, StringComparison.Ordinal);
        Assert.Contains("minimumSchemaVersion", script, StringComparison.Ordinal);
        Assert.Contains("targetSchemaVersion", script, StringComparison.Ordinal);
        Assert.Contains("DatabaseSchema.ExpectedVersion", script, StringComparison.Ordinal);
        Assert.Contains("Assert-ReleaseArchive", script, StringComparison.Ordinal);
        Assert.Contains("provision-database-runtime-roles.ps1", script, StringComparison.Ordinal);
        Assert.Contains("Get-CanonicalBuildVersion", script, StringComparison.Ordinal);
        Assert.Contains("Nerdbank.GitVersioning", script, StringComparison.Ordinal);
        Assert.Contains("ReplaceExistingArtifact", script, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", script, StringComparison.Ordinal);
        Assert.Contains("New-OrderedZipArchive", script, StringComparison.Ordinal);
        Assert.Contains("Assert-ReleaseStagingTree", script, StringComparison.Ordinal);
        Assert.Contains("Assert-SafeConfigurationJson", script, StringComparison.Ordinal);
        Assert.Contains("builtAtUtc", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Compress-Archive", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task InputValidation_RejectsDuplicateMigrationVersions()
    {
        using var fixture = CreateInputValidationFixture();
        File.Copy(
            Path.Combine(fixture.Root, "db", "migrations", "0001_baseline.sql"),
            Path.Combine(fixture.Root, "db", "migrations", "0001_duplicate.sql"));

        var result = await RunPowerShellAsync(
            fixture.Root,
            Path.Combine(fixture.Root, "Scripts", "build-release.ps1"),
            "-ValidatePackageInputsOnly");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Duplicate database migration version", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task InputValidation_RejectsMissingBaseline()
    {
        using var fixture = CreateInputValidationFixture();
        File.Delete(Path.Combine(fixture.Root, "db", "migrations", "0001_baseline.sql"));

        var result = await RunPowerShellAsync(
            fixture.Root,
            Path.Combine(fixture.Root, "Scripts", "build-release.ps1"),
            "-ValidatePackageInputsOnly");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Required database release file not found", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BuildRelease_FromDifferentWorkingDirectory_EnforcesReplacementAndProducesValidatedArchive()
    {
        var root = ResolveRepositoryRoot();
        var output = Path.Combine(Path.GetTempPath(), $"pricecrawler-release-{Guid.NewGuid():N}");
        var callerDirectory = Path.Combine(Path.GetTempPath(), $"pricecrawler-caller-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(callerDirectory);
        const string version = "v9.9.9-mpc82-test";
        var archivePath = Path.Combine(output, $"PriceCrawler-{version}.zip");
        var checksumPath = archivePath + ".sha256";
        await File.WriteAllTextAsync(archivePath, "existing release must not be overwritten silently");
        await File.WriteAllTextAsync(checksumPath, "existing checksum");

        try
        {
            var refusal = await RunPowerShellAsync(
                callerDirectory,
                Path.Combine(root, "Scripts", "build-release.ps1"),
                "-Configuration", "Mpc82PackageTest",
                "-Version", version,
                "-OutputDirectory", output,
                "-SkipTests",
                "-AllowDirtyWorkingTree");
            Assert.NotEqual(0, refusal.ExitCode);
            Assert.Contains("Refusing to overwrite", refusal.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("existing release must not be overwritten silently", await File.ReadAllTextAsync(archivePath));

            var result = await RunPowerShellAsync(
                callerDirectory,
                Path.Combine(root, "Scripts", "build-release.ps1"),
                "-Configuration", "Mpc82PackageTest",
                "-Version", version,
                "-OutputDirectory", output,
                "-ReplaceExistingArtifact",
                "-SkipTests",
                "-AllowDirtyWorkingTree");
            Assert.True(result.ExitCode == 0, result.CombinedOutput);
            Assert.True(File.Exists(archivePath));
            Assert.True(File.Exists(checksumPath));

            using var archive = ZipFile.OpenRead(archivePath);
            var entries = archive.Entries.Select(entry => entry.FullName).ToArray();
            Assert.Contains("release.json", entries);
            Assert.Contains("db/migrations/0001_baseline.sql", entries);
            Assert.Contains("db/scripts/bootstrap-schema-version.sql", entries);
            Assert.Contains("db/scripts/provision-database-runtime-roles.ps1", entries);
            Assert.Contains("web/PriceCrawler.Web.dll", entries);
            Assert.Contains("crawler/PriceCrawler.Worker.dll", entries);
            Assert.DoesNotContain(entries, path => path.Contains('\\'));
            Assert.DoesNotContain(entries, IsForbiddenPackagePath);

            var releaseEntry = archive.GetEntry("release.json")!;
            await using var releaseStream = releaseEntry.Open();
            using var metadata = await JsonDocument.ParseAsync(releaseStream);
            var rootElement = metadata.RootElement;
            Assert.Equal("PriceCrawler", rootElement.GetProperty("product").GetString());
            Assert.Equal(version, rootElement.GetProperty("version").GetString());
            Assert.Equal(40, rootElement.GetProperty("commit").GetString()!.Length);
            Assert.EndsWith("Z", rootElement.GetProperty("builtAtUtc").GetString(), StringComparison.Ordinal);
            Assert.Equal(1, rootElement.GetProperty("database").GetProperty("minimumSchemaVersion").GetInt32());
            Assert.Equal(1, rootElement.GetProperty("database").GetProperty("targetSchemaVersion").GetInt32());
            Assert.True(rootElement.GetProperty("components").GetProperty("web").GetBoolean());
            Assert.True(rootElement.GetProperty("components").GetProperty("crawler").GetBoolean());

            foreach (var settings in archive.Entries.Where(entry => entry.Name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)))
            {
                using var reader = new StreamReader(settings.Open());
                var json = await reader.ReadToEndAsync();
                Assert.DoesNotContain("myPassword", json, StringComparison.Ordinal);
                if (settings.Name.Contains("Stage", StringComparison.OrdinalIgnoreCase) ||
                    settings.Name.Contains("Production", StringComparison.OrdinalIgnoreCase))
                    Assert.Contains("ValidateOnly", json, StringComparison.Ordinal);
            }

            await using var archiveHashStream = File.OpenRead(archivePath);
            var expectedHash = Convert.ToHexString(await SHA256.HashDataAsync(archiveHashStream)).ToLowerInvariant();
            var recordedHash = (await File.ReadAllTextAsync(checksumPath)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
            Assert.Equal(expectedHash, recordedHash);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
            Directory.Delete(callerDirectory, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WebAndWorkerStartup_UseSharedCoordinatorWithoutPrivateBootstrapPath()
    {
        var root = ResolveRepositoryRoot();
        foreach (var host in new[] { "PriceCrawler.Web", "PriceCrawler.Worker" })
        {
            var program = File.ReadAllText(Path.Combine(root, host, "Program.cs"));
            Assert.Contains("DatabaseSchemaStartupCoordinator", program, StringComparison.Ordinal);
            Assert.Contains("databaseStartup.ExecuteAsync", program, StringComparison.Ordinal);
            Assert.DoesNotContain("SchemaBootstrapper", program, StringComparison.Ordinal);
            Assert.DoesNotContain("EnsureSchemaAsync", program, StringComparison.Ordinal);

            var project = File.ReadAllText(Path.Combine(root, host, $"{host}.csproj"));
            Assert.Contains("db\\migrations\\0001_baseline.sql", project, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidateOnlyPath_IsSeparatedFromAllMutationServices()
    {
        var persistence = Path.Combine(ResolveRepositoryRoot(), "PriceCrawler.Infrastructure", "Persistence");
        var validator = File.ReadAllText(Path.Combine(persistence, "DatabaseSchemaValidator.cs"));
        var reader = File.ReadAllText(Path.Combine(persistence, "DatabaseSchemaVersionReader.cs"));

        Assert.Contains("versionReader.ReadAsync", validator, StringComparison.Ordinal);
        Assert.DoesNotContain("DatabaseSchemaInitializer", validator, StringComparison.Ordinal);
        Assert.DoesNotContain("SchemaBootstrapper", validator, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteNonQuery", validator, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteNonQuery", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("insert ", reader, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create ", reader, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alter ", reader, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drop ", reader, StringComparison.OrdinalIgnoreCase);
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

    private static bool IsForbiddenPackagePath(string path)
    {
        var normalized = "/" + path.Replace('\\', '/').ToLowerInvariant();
        return normalized.Contains("/.git/") ||
               normalized.Contains("/graphify-out/") ||
               normalized.Contains("/.code-review-graph/") ||
               normalized.Contains("/testresults/") ||
               normalized.Contains("/backups/") ||
               normalized.Contains("/logs/") ||
               normalized.EndsWith(".dump", StringComparison.Ordinal) ||
               normalized.EndsWith(".backup", StringComparison.Ordinal) ||
               normalized.EndsWith(".log", StringComparison.Ordinal) ||
               normalized.EndsWith("/.env", StringComparison.Ordinal) ||
               normalized.EndsWith("/.pgpass", StringComparison.Ordinal);
    }

    private static async Task<ProcessResult> RunPowerShellAsync(
        string workingDirectory,
        string scriptPath,
        params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("powershell.exe")
            {
                WorkingDirectory = workingDirectory,
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
        process.StartInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        process.StartInfo.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private static InputValidationFixture CreateInputValidationFixture()
    {
        var sourceRoot = ResolveRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), $"pricecrawler-release-inputs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "Scripts"));
        Directory.CreateDirectory(Path.Combine(root, "PriceCrawler.Application"));
        Directory.CreateDirectory(Path.Combine(root, "PriceCrawler.Web"));
        Directory.CreateDirectory(Path.Combine(root, "PriceCrawler.Worker"));
        Directory.CreateDirectory(Path.Combine(root, "PriceCrawler.Infrastructure", "Persistence"));
        Directory.CreateDirectory(Path.Combine(root, "db", "migrations"));
        Directory.CreateDirectory(Path.Combine(root, "db", "scripts"));
        File.Copy(Path.Combine(sourceRoot, "Scripts", "build-release.ps1"), Path.Combine(root, "Scripts", "build-release.ps1"));
        File.WriteAllText(Path.Combine(root, "PriceCrawler.sln"), "fixture");
        File.WriteAllText(Path.Combine(root, "PriceCrawler.Application", "PriceCrawler.Application.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(root, "PriceCrawler.Web", "PriceCrawler.Web.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(root, "PriceCrawler.Worker", "PriceCrawler.Worker.csproj"), "<Project />");
        File.WriteAllText(
            Path.Combine(root, "PriceCrawler.Infrastructure", "Persistence", "DatabaseSchema.cs"),
            "public static class DatabaseSchema { public const int ExpectedVersion = 1; }");
        File.WriteAllText(Path.Combine(root, "Scripts", "provision-database-runtime-roles.ps1"), "# fixture");
        const string schemaMetadata = "insert into public.schema_version(version, migration_name) values (1, '0001_baseline');";
        File.WriteAllText(Path.Combine(root, "db", "migrations", "0001_baseline.sql"), schemaMetadata);
        File.WriteAllText(Path.Combine(root, "db", "scripts", "bootstrap-schema-version.sql"), schemaMetadata);
        return new InputValidationFixture(root);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => StandardOutput + Environment.NewLine + StandardError;
    }

    private sealed class InputValidationFixture(string root) : IDisposable
    {
        public string Root { get; } = root;
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
