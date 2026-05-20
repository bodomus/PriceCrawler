using Microsoft.Extensions.Logging;

using VarPrice.Application.Models;

namespace VarPrice.Application.UseCases;

public static class ProductUrlFiltering
{
    public static IReadOnlyCollection<Uri> Apply(
        IEnumerable<string> urls,
        CrawlerOptions crawlerOptions,
        UrlFilterOptions urlFilterOptions,
        ILogger logger,
        string sourceName)
    {
        var excluded = urlFilterOptions.ExcludedUrlSubstrings;
        var results = new List<Uri>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxProductsPerRun = Math.Max(1, crawlerOptions.MaxProductsPerRun);
        var maxUrls = Math.Max(1, crawlerOptions.MaxUrls);
        var limit = Math.Min(maxProductsPerRun, maxUrls);

        foreach (var rawUrl in urls)
        {
            if (results.Count >= limit)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                continue;
            }

            var url = rawUrl.Trim();
            if (!string.IsNullOrWhiteSpace(crawlerOptions.VegetablesUrlContains) &&
                !url.Contains(crawlerOptions.VegetablesUrlContains, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (excluded.Length > 0 &&
                excluded.Any(ex => url.Contains(ex, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                logger.LogWarning(
                    "Product URL rejected. Source={Source}; Url={Url}; Reason=InvalidAbsoluteUrl",
                    sourceName,
                    url);
                continue;
            }

            var normalized = NormalizeProductUrl(uri);
            if (seen.Add(normalized.AbsoluteUri))
            {
                results.Add(normalized);
            }
        }

        return results;
    }

    public static Uri NormalizeProductUrl(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
            Query = string.Empty
        };

        return builder.Uri;
    }
}
