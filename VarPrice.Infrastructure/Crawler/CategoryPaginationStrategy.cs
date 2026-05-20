using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace VarPrice.Infrastructure.Crawler;

public sealed class CategoryPaginationStrategy : ICategoryPaginationStrategy
{
    public Uri? GetNextPageUrl(string html, Uri currentPageUrl, ISet<string> visitedPageUrls)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);
        IEnumerable<IElement> candidates = document.QuerySelectorAll(
            "a[rel='next' i][href], a.next[href], .next a[href], .pagination-next[href], .pagination-next a[href], [aria-label*='next' i][href]");

        if (!candidates.Any())
        {
            candidates = document.QuerySelectorAll("a[href]")
                .Where(anchor => IsNextPageText(anchor.TextContent));
        }

        foreach (var candidate in candidates)
        {
            var href = candidate.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href) ||
                !Uri.TryCreate(currentPageUrl, href.Trim(), out var nextPageUrl) ||
                !VarusUrlRules.IsVarusHttpsUrl(nextPageUrl))
            {
                continue;
            }

            var normalized = NormalizePageUrl(nextPageUrl);
            if (!visitedPageUrls.Contains(normalized.AbsoluteUri))
            {
                return normalized;
            }
        }

        return null;
    }

    public string? ResolveStopReason(int newProductUrls, Uri? nextPageUrl, int pageNumber, int maxPagesPerSeed)
    {
        if (newProductUrls == 0)
        {
            return CategoryDiscoveryStopReasons.NoNewProductUrls;
        }

        if (nextPageUrl is null)
        {
            return CategoryDiscoveryStopReasons.NoNextPage;
        }

        if (pageNumber >= maxPagesPerSeed)
        {
            return CategoryDiscoveryStopReasons.MaxCategoryPagesPerSeed;
        }

        return null;
    }

    private static bool IsNextPageText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        return value is ">" or "›" or "»" ||
               value.Contains("next", StringComparison.OrdinalIgnoreCase);
    }

    private static Uri NormalizePageUrl(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty
        };

        return builder.Uri;
    }
}
