using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using VarPrice.Application.Models;

namespace VarPrice.Infrastructure.Crawler;

public sealed class CategorySeedProvider(
    IOptions<CategorySeedUrlFileOptions> seedFileOptions,
    ILogger<CategorySeedProvider> logger) : ICategorySeedProvider
{
    public async Task<IReadOnlyList<CategorySeedUrl>> GetSeedsAsync(CancellationToken ct)
    {
        var path = seedFileOptions.Value.ResolvedPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            logger.LogWarning(
                "Category seed URL discovery unavailable. Reason=CategorySeedFilePathMissing; ConfigurationKey=Crawler:CategorySeedUrlsFilePath");
            return [];
        }

        logger.LogInformation("Loading category seed URLs. Path={Path}", seedFileOptions.Value.PathSetting);

        if (!File.Exists(path))
        {
            logger.LogWarning(
                "Category seed URL discovery unavailable. Reason=CategorySeedFileMissing; FilePath={FilePath}",
                path);
            return [];
        }

        CategorySeedConfig? config;
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            config = JsonSerializer.Deserialize<CategorySeedConfig>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "Category seed URL discovery unavailable. Reason=CategorySeedFileInvalid; FilePath={FilePath}",
                path);
            return [];
        }

        var entries = config?.Crawler?.CategorySeedUrls ?? [];
        var results = new List<CategorySeedUrl>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var name = entry.Name?.Trim() ?? string.Empty;
            var url = entry.Url?.Trim() ?? string.Empty;
            var rejectionReason = ValidateSeed(name, url, out var uri);
            if (rejectionReason is not null || uri is null)
            {
                logger.LogWarning(
                    "Category seed URL rejected. Name={Name}; Url={Url}; Reason={Reason}",
                    name,
                    url,
                    rejectionReason);
                continue;
            }

            var normalizedUrl = NormalizeSeedUrl(uri).AbsoluteUri;
            if (seen.Add(normalizedUrl))
            {
                results.Add(new CategorySeedUrl(name, new Uri(normalizedUrl)));
            }
        }

        logger.LogInformation("Category seed URLs loaded. FilePath={FilePath}; Count={Count}", path, results.Count);
        return results;
    }

    private static string? ValidateSeed(string name, string url, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            return "EmptyName";
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return "EmptyUrl";
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
        {
            return "MalformedUrl";
        }

        if (!VarusUrlRules.IsVarusHttpsUrl(uri))
        {
            return "NotVarusHttpsUrl";
        }

        return null;
    }

    private static Uri NormalizeSeedUrl(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
            Query = string.Empty
        };

        return builder.Uri;
    }

    private sealed record CategorySeedConfig(CategorySeedCrawlerSection? Crawler);

    private sealed record CategorySeedCrawlerSection(IReadOnlyList<CategorySeedEntry>? CategorySeedUrls);

    private sealed record CategorySeedEntry(string? Name, string? Url);
}
