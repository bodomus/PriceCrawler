using System.Net;
using System.Text.Json;

using AngleSharp.Dom;
using AngleSharp.Html.Parser;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;
using VarPrice.Application.UseCases;

namespace VarPrice.Infrastructure.Crawler;

public sealed class CategoryProductUrlDiscoverySource(
    IHttpClientFactory httpClientFactory,
    VarusRequestCoordinator requestCoordinator,
    IOptions<CrawlerOptions> crawlerOptions,
    IOptions<UrlFilterOptions> urlFilterOptions,
    IOptions<CategorySeedUrlFileOptions> seedFileOptions,
    ILogger<CategoryProductUrlDiscoverySource> logger) : ICategoryProductUrlDiscoverySource
{
    private static readonly Uri VarusBaseUri = new("https://varus.ua/");

    public async Task<IReadOnlyCollection<Uri>> DiscoverProductUrlsAsync(CancellationToken ct)
    {
        var seeds = await LoadSeedsAsync(ct);
        if (seeds.Count == 0)
        {
            logger.LogWarning("Category seed URL discovery unavailable. Reason=CategorySeedFileEmpty");
            return [];
        }

        var rawProductUrls = new List<string>();
        var discoveredUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxPagesPerSeed = Math.Max(1, crawlerOptions.Value.MaxCategoryPagesPerSeed);
        foreach (var seed in seeds)
        {
            ct.ThrowIfCancellationRequested();

            var pageNumber = 1;
            var pageUrl = seed.Url;
            var visitedPageUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                visitedPageUrls.Add(pageUrl.AbsoluteUri);

                logger.LogInformation(
                    "Loading category page. Name={Name}; SeedUrl={SeedUrl}; PageUrl={PageUrl}; PageNumber={PageNumber}",
                    seed.Name,
                    seed.Url,
                    pageUrl,
                    pageNumber);

                var html = await LoadCategoryHtmlAsync(seed, pageUrl, ct);
                if (string.IsNullOrWhiteSpace(html))
                {
                    LogCategoryPageProcessed(seed, pageUrl, pageNumber, 0, 0, "PageUnavailable");
                    break;
                }

                var extracted = ExtractProductUrls(html, pageUrl);
                var newUrls = 0;
                foreach (var productUrl in extracted)
                {
                    if (discoveredUrls.Add(productUrl.AbsoluteUri))
                    {
                        rawProductUrls.Add(productUrl.AbsoluteUri);
                        newUrls++;
                    }
                }

                var nextPageUrl = ExtractNextPageUrl(html, pageUrl, visitedPageUrls);
                var stopReason = ResolveStopReason(newUrls, nextPageUrl, pageNumber, maxPagesPerSeed);
                LogCategoryPageProcessed(seed, pageUrl, pageNumber, extracted.Count, newUrls, stopReason);

                if (stopReason is not null)
                {
                    break;
                }

                pageUrl = nextPageUrl!;
                pageNumber++;
            }
        }

        var filtered = ProductUrlFiltering.Apply(
            rawProductUrls,
            crawlerOptions.Value,
            urlFilterOptions.Value,
            logger,
            sourceName: "category-seed");

        logger.LogInformation(
            "Product URL discovery completed using category seed fallback. SeedCategoryCount={SeedCategoryCount}; ProductUrlCount={ProductUrlCount}",
            seeds.Count,
            filtered.Count);

        return filtered;
    }

    public static Uri? ExtractNextPageUrl(string html, Uri currentPageUrl, ISet<string>? visitedPageUrls = null)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);
        IEnumerable<IElement> candidates = document.QuerySelectorAll(
            "a[rel='next' i][href], a.next[href], .next a[href], .pagination-next[href], .pagination-next a[href], [aria-label*='next' i][href], [aria-label*='наступ' i][href]");

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
                !IsVarusHttpsUrl(nextPageUrl))
            {
                continue;
            }

            var normalized = NormalizePageUrl(nextPageUrl);
            if (visitedPageUrls is not null && visitedPageUrls.Contains(normalized.AbsoluteUri))
            {
                continue;
            }

            return normalized;
        }

        return null;
    }

    public static IReadOnlyCollection<Uri> ExtractProductUrls(string html, Uri categoryUrl)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var anchor in SelectProductAnchors(document))
        {
            var href = anchor.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            if (!Uri.TryCreate(VarusBaseUri, href.Trim(), out var uri))
            {
                continue;
            }

            if (!IsVarusHttpsUrl(uri) || !LooksLikeProductUrl(uri, categoryUrl))
            {
                continue;
            }

            urls.Add(ProductUrlFiltering.NormalizeProductUrl(uri).AbsoluteUri);
        }

        return urls.Select(x => new Uri(x)).ToList();
    }

    private async Task<IReadOnlyList<CategorySeedUrl>> LoadSeedsAsync(CancellationToken ct)
    {
        var path = seedFileOptions.Value.ResolvedPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            logger.LogWarning(
                "Category seed URL discovery unavailable. Reason=CategorySeedFilePathMissing; ConfigurationKey=Crawler:CategorySeedUrlsFilePath");
            return [];
        }

        logger.LogInformation(
            "Loading category seed URLs. Path={Path}",
            seedFileOptions.Value.PathSetting);

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

            var normalizedUrl = ProductUrlFiltering.NormalizeProductUrl(uri).AbsoluteUri;
            if (seen.Add(normalizedUrl))
            {
                results.Add(new CategorySeedUrl(name, new Uri(normalizedUrl)));
            }
        }

        logger.LogInformation(
            "Category seed URLs loaded. FilePath={FilePath}; Count={Count}",
            path,
            results.Count);

        return results;
    }

    private async Task<string?> LoadCategoryHtmlAsync(CategorySeedUrl seed, Uri pageUrl, CancellationToken ct)
    {
        await requestCoordinator.AcquireRequestSlotAsync(ct);
        var http = httpClientFactory.CreateClient("varus");
        using var request = new HttpRequestMessage(HttpMethod.Get, pageUrl);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            logger.LogWarning(
                "Category page skipped. Name={Name}; SeedUrl={SeedUrl}; PageUrl={PageUrl}; FailureKind={FailureKind}; HttpStatus={HttpStatus}",
                seed.Name,
                seed.Url,
                pageUrl,
                ClassifyStatus(response.StatusCode),
                (int)response.StatusCode);
            return null;
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Category page skipped. Name={Name}; SeedUrl={SeedUrl}; PageUrl={PageUrl}; FailureKind=CategoryPageInvalidContentType; ContentType={ContentType}",
                seed.Name,
                seed.Url,
                pageUrl,
                contentType);
            return null;
        }

        var html = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(html))
        {
            logger.LogWarning(
                "Category page skipped. Name={Name}; SeedUrl={SeedUrl}; PageUrl={PageUrl}; FailureKind=CategoryPageEmptyBody",
                seed.Name,
                seed.Url,
                pageUrl);
            return null;
        }

        return html;
    }

    private void LogCategoryPageProcessed(
        CategorySeedUrl seed,
        Uri pageUrl,
        int pageNumber,
        int productUrlsFound,
        int newProductUrls,
        string? stopReason)
    {
        logger.LogInformation(
            "Category seed page processed. SeedUrl={SeedUrl}; PageUrl={PageUrl}; PageNumber={PageNumber}; ProductUrlsFound={ProductUrlsFound}; NewProductUrls={NewProductUrls}; StopReason={StopReason}",
            seed.Url,
            pageUrl,
            pageNumber,
            productUrlsFound,
            newProductUrls,
            stopReason ?? string.Empty);
    }

    private static string? ResolveStopReason(int newUrls, Uri? nextPageUrl, int pageNumber, int maxPagesPerSeed)
    {
        if (newUrls == 0)
        {
            return "NoNewProductUrls";
        }

        if (nextPageUrl is null)
        {
            return "NoNextPage";
        }

        if (pageNumber >= maxPagesPerSeed)
        {
            return "MaxCategoryPagesPerSeed";
        }

        return null;
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

        if (!IsVarusHttpsUrl(uri))
        {
            return "NotVarusHttpsUrl";
        }

        return null;
    }

    private static bool IsVarusHttpsUrl(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(uri.Host, "varus.ua", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<IElement> SelectProductAnchors(IDocument document)
    {
        var cardAnchors = document.QuerySelectorAll(
            "[class*='product-card' i] a[href], [class*='product_tile' i] a[href], [data-testid*='product' i] a[href]");
        if (cardAnchors.Length > 0)
        {
            return cardAnchors;
        }

        return document.QuerySelectorAll("a[href]");
    }

    private static bool LooksLikeProductUrl(Uri uri, Uri categoryUrl)
    {
        var path = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (string.Equals(uri.AbsolutePath, categoryUrl.AbsolutePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (path.Contains('/'))
        {
            return false;
        }

        if (path.StartsWith("brands/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("blog", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("checkout", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("customer", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("img/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("media/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsNextPageText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        return value is ">" or "›" or "»" ||
               value.Contains("next", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("наступ", StringComparison.OrdinalIgnoreCase);
    }

    private static Uri NormalizePageUrl(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty
        };

        return builder.Uri;
    }

    private static string ClassifyStatus(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.NotFound => "CategoryPageNotFound",
            HttpStatusCode.Forbidden => "CategoryPageForbidden",
            HttpStatusCode.TooManyRequests => "CategoryPageRateLimited",
            _ when (int)statusCode >= 500 => "CategoryPageServerError",
            _ => "CategoryPageHttpError"
        };

    private sealed record CategorySeedConfig(CategorySeedCrawlerSection? Crawler);

    private sealed record CategorySeedCrawlerSection(IReadOnlyList<CategorySeedEntry>? CategorySeedUrls);

    private sealed record CategorySeedEntry(string? Name, string? Url);

    private sealed record CategorySeedUrl(string Name, Uri Url);
}
