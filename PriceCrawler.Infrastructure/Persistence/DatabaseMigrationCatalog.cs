using System.Globalization;
using System.Text.RegularExpressions;

namespace PriceCrawler.Infrastructure.Persistence;

/// <summary>A numbered forward migration from <c>db/migrations</c>.</summary>
/// <param name="Version">Schema version the migration produces.</param>
/// <param name="Name">File name without extension, e.g. <c>0002_category_tree</c>.</param>
/// <param name="FullPath">Absolute path of the SQL file.</param>
public sealed record DatabaseMigration(int Version, string Name, string FullPath);

/// <summary>Discovers and validates the numbered forward migrations shipped with the application.</summary>
public static partial class DatabaseMigrationCatalog
{
    /// <summary>Loads every migration after the baseline, ordered by version.</summary>
    /// <returns>Migrations with versions <c>BaselineVersion + 1 .. ExpectedVersion</c>, contiguous.</returns>
    /// <exception cref="InvalidOperationException">
    /// A file name breaks the <c>NNNN_description.sql</c> convention, versions have gaps or duplicates,
    /// or the last version differs from <see cref="DatabaseSchema.ExpectedVersion"/>.
    /// </exception>
    public static IReadOnlyList<DatabaseMigration> LoadForwardMigrations()
        => LoadForwardMigrations(SqlAssetLocator.ResolveMigrationsDirectory(), DatabaseSchema.ExpectedVersion);

    /// <summary>Loads and validates the forward migrations of the given directory.</summary>
    /// <param name="migrationsDirectory">Directory that holds <c>NNNN_description.sql</c> files.</param>
    /// <param name="expectedVersion">Version the newest migration must produce.</param>
    /// <returns>Migrations after the baseline, ordered by version.</returns>
    /// <exception cref="InvalidOperationException">The directory content is inconsistent.</exception>
    public static IReadOnlyList<DatabaseMigration> LoadForwardMigrations(string migrationsDirectory, int expectedVersion)
    {
        var all = Directory.GetFiles(migrationsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .Select(path => Parse(path))
            .OrderBy(migration => migration.Version)
            .ToList();

        for (var index = 0; index < all.Count; index++)
        {
            var expected = DatabaseSchema.BaselineVersion + index;
            if (all[index].Version != expected)
            {
                throw new InvalidOperationException(
                    $"Database migrations must be contiguous from {DatabaseSchema.BaselineVersion:0000}; " +
                    $"found '{all[index].Name}' where version {expected} was expected.");
            }
        }

        var latest = all.Count == 0 ? 0 : all[^1].Version;
        if (latest != expectedVersion)
        {
            throw new InvalidOperationException(
                $"Newest database migration produces version {latest}, but the application expects {expectedVersion}.");
        }

        return all.Where(migration => migration.Version > DatabaseSchema.BaselineVersion).ToList();
    }

    private static DatabaseMigration Parse(string path)
    {
        var fileName = Path.GetFileName(path);
        var match = FileNameRegex().Match(fileName);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Database migration '{fileName}' must match the 'NNNN_description.sql' convention.");
        }

        return new DatabaseMigration(
            int.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture),
            Path.GetFileNameWithoutExtension(fileName),
            path);
    }

    [GeneratedRegex(@"^(?<version>\d{4})_[a-z0-9_]+\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNameRegex();
}
