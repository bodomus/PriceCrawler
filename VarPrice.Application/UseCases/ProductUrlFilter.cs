using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;

namespace VarPrice.Application.UseCases;

public sealed class ProductUrlFilter(
    IOptions<CrawlerOptions> crawlerOptions,
    IOptions<UrlFilterOptions> urlFilterOptions,
    ILogger<ProductUrlFilter> logger) : IProductUrlFilter
{
    public IReadOnlyList<string> Apply(IEnumerable<Uri> urls, string sourceName)
    {
        var opt = crawlerOptions.Value;
        var excluded = urlFilterOptions.Value.ExcludedUrlSubstrings;
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var limit = Math.Min(Math.Max(1, opt.MaxProductsPerRun), Math.Max(1, opt.MaxUrls));

        foreach (var uri in urls)
        {
            if (results.Count >= limit)
            {
                break;
            }

            var normalized = NormalizeProductUrl(uri).AbsoluteUri;
            if (!string.IsNullOrWhiteSpace(opt.VegetablesUrlContains) &&
                !normalized.Contains(opt.VegetablesUrlContains, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (excluded.Length > 0 &&
                excluded.Any(ex => normalized.Contains(ex, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (seen.Add(normalized))
            {
                results.Add(normalized);
            }
        }

        logger.LogInformation(
            "Product URL filtering completed. Source={Source}; Count={Count}",
            sourceName,
            results.Count);
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
