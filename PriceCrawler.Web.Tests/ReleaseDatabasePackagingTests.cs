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
        Assert.Contains("Assert-ReleaseArchiveDatabaseAssets", script, StringComparison.Ordinal);
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
}
