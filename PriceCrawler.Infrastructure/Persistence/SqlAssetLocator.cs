namespace PriceCrawler.Infrastructure.Persistence;

public static class SqlAssetLocator
{
    public static string ResolveSchemaPath()
        => ResolveFile("schema.sql");

    public static string ResolveBaselinePath()
        => ResolveFile("db", "migrations", "0001_baseline.sql");

    public static string ResolveRoutineScriptsDirectory()
        => ResolveDirectory("db", "routines");

    private static string ResolveFile(params string[] segments)
    {
        foreach (var candidate in GetCandidates())
        {
            var directory = new DirectoryInfo(candidate);
            while (directory is not null)
            {
                var filePath = Path.Combine([directory.FullName, .. segments]);
                if (File.Exists(filePath))
                {
                    return filePath;
                }

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(segments)}.");
    }

    private static string ResolveDirectory(params string[] segments)
    {
        foreach (var candidate in GetCandidates())
        {
            var directory = new DirectoryInfo(candidate);
            while (directory is not null)
            {
                var targetPath = Path.Combine([directory.FullName, .. segments]);
                if (Directory.Exists(targetPath))
                {
                    return targetPath;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException($"Could not locate {Path.Combine(segments)}.");
    }

    private static IEnumerable<string> GetCandidates()
    {
        yield return Directory.GetCurrentDirectory();
        yield return AppContext.BaseDirectory;
    }
}
