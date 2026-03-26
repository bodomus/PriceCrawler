using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace VarPrice.Infrastructure.Persistence;

public static partial class DbRoutineScriptCatalog
{
    private static readonly Regex FileNamePattern = RoutineScriptFileNameRegex();

    public static async Task<IReadOnlyList<DbRoutineScript>> LoadAsync(CancellationToken ct = default)
    {
        var routinesDirectory = SqlAssetLocator.ResolveRoutineScriptsDirectory();
        var files = Directory.GetFiles(routinesDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();

        var scripts = new List<DbRoutineScript>(files.Length);
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            ValidateFileName(fileName);

            var sql = await File.ReadAllTextAsync(file, ct);
            scripts.Add(new DbRoutineScript(fileName, file, sql, ComputeHash(sql)));
        }

        return scripts;
    }

    private static void ValidateFileName(string fileName)
    {
        if (!FileNamePattern.IsMatch(fileName))
        {
            throw new InvalidOperationException(
                $"Routine script '{fileName}' must match the 'NNN__description.sql' convention.");
        }
    }

    private static string ComputeHash(string sql)
    {
        var bytes = Encoding.UTF8.GetBytes(sql);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [GeneratedRegex(@"^\d{3}__[a-z0-9_]+\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex RoutineScriptFileNameRegex();
}

public sealed record DbRoutineScript(string Name, string FullPath, string Sql, string Hash);
