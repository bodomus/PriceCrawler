using System.Xml.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;

namespace VarPrice.Infrastructure.Crawler;

public sealed class SitemapReader : IProductUrlSource
{
    private const int DefaultMaxSitemapsToVisit = 10;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<CrawlerOptions> _crawlerOptions;
    private readonly IOptions<UrlFilterOptions> _urlFilterOptions;
    private readonly ISitemapHttpClient _sitemapHttpClient;
    private readonly ISitemapResponseValidator _sitemapResponseValidator;
    private readonly SitemapDiscoveryService _sitemapDiscoveryService;
    private readonly ILogger<SitemapReader> _log;

    public SitemapReader(
        IHttpClientFactory httpClientFactory,
        IOptions<CrawlerOptions> crawlerOptions,
        IOptions<UrlFilterOptions> urlFilterOptions,
        ILogger<SitemapReader> log)
        : this(
            httpClientFactory,
            crawlerOptions,
            urlFilterOptions,
            new SitemapHttpClient(),
            new SitemapResponseValidator(),
            CreateDefaultDiscoveryService(),
            log)
    {
    }

    public SitemapReader(
        IHttpClientFactory httpClientFactory,
        IOptions<CrawlerOptions> crawlerOptions,
        IOptions<UrlFilterOptions> urlFilterOptions,
        ISitemapHttpClient sitemapHttpClient,
        ISitemapResponseValidator sitemapResponseValidator,
        SitemapDiscoveryService sitemapDiscoveryService,
        ILogger<SitemapReader> log)
    {
        _httpClientFactory = httpClientFactory;
        _crawlerOptions = crawlerOptions;
        _urlFilterOptions = urlFilterOptions;
        _sitemapHttpClient = sitemapHttpClient;
        _sitemapResponseValidator = sitemapResponseValidator;
        _sitemapDiscoveryService = sitemapDiscoveryService;
        _log = log;
    }

    public async Task<IReadOnlyList<string>> GetProductUrlsAsync(string sitemapIndexUrl, CancellationToken ct)
    {
        var http = _httpClientFactory.CreateClient("varus");
        var discovery = await _sitemapDiscoveryService.DiscoverAsync(http, sitemapIndexUrl, ct);
        var doc = discovery.Document;
        var rootSitemapUrl = discovery.Url;
        var maxUrls = _crawlerOptions.Value.MaxUrls;
        if (IsUrlSet(doc))
        {
            var locs = GetLocs(doc);
            _log.LogInformation("Sitemap processed: Url={Url}, RootType=urlset, LocCount={LocCount}", rootSitemapUrl,
                locs.Count);
            var urls = new HashSet<string>(StringComparer.Ordinal);
            foreach (var url in locs)
            {
                if (urls.Count >= maxUrls)
                {
                    _log.LogWarning("Reached maxUrls={MaxUrls} while processing {Url}", maxUrls, rootSitemapUrl);
                    break;
                }

                urls.Add(url);
            }

            return urls.ToList();
        }

        if (IsSitemapIndex(doc))
        {
            var sitemapLocs = GetLocs(doc);
            _log.LogInformation("Sitemap processed: Url={Url}, RootType=sitemapindex, LocCount={LocCount}",
                rootSitemapUrl, sitemapLocs.Count);
            return await CollectUrlsAsync(http, sitemapLocs, ct, maxUrls, DefaultMaxSitemapsToVisit);
        }

        var root = doc.Root?.Name.LocalName ?? "<null>";
        throw new InvalidOperationException($"Unknown sitemap root '{root}' at '{rootSitemapUrl}'.");
    }

    public async Task<XDocument> LoadXmlAsync(HttpClient http, string url, CancellationToken ct)
    {
        var response = await _sitemapHttpClient.GetAsync(http, url, ct);
        var validation = _sitemapResponseValidator.Validate(response);
        if (validation.IsValid && validation.Document is not null)
        {
            return validation.Document;
        }

        _log.LogError(
            "Failed to load sitemap XML. Url={Url}, StatusCode={StatusCode}, FailureKind={FailureKind}, ContentType={ContentType}, ContentEncoding={ContentEncoding}, Preview={Preview}",
            response.Url,
            (int)response.StatusCode,
            validation.FailureKind,
            response.ContentType,
            response.ContentEncoding,
            response.BodyPreview);
        throw new InvalidOperationException(
            $"Failed to load sitemap XML from '{url}'. FailureKind={validation.FailureKind}. {validation.Message}");
    }

    public static bool IsSitemapIndex(XDocument doc) => doc.Root?.Name.LocalName == "sitemapindex";

    public static bool IsUrlSet(XDocument doc) => doc.Root?.Name.LocalName == "urlset";

    public List<string> GetLocs(XDocument doc)
    {
        var excluded = _urlFilterOptions.Value.ExcludedUrlSubstrings;
        if (doc.Root is null)
        {
            return [];
        }

        var ns = doc.Root.Name.Namespace;
        return doc
            .Descendants(ns + "loc")
            .Select(x => x.Value.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(a => !excluded.Any(ex => a.Contains(ex, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public async Task<List<string>> CollectUrlsAsync(
        HttpClient http,
        IEnumerable<string> sitemapLocs,
        CancellationToken ct,
        int? maxUrls = null,
        int? maxSitemapsToVisit = null)
    {
        var maxUrlsLimit = maxUrls ?? _crawlerOptions.Value.MaxUrls;
        var results = new HashSet<string>(StringComparer.Ordinal);
        var topLevelSitemaps = maxSitemapsToVisit.HasValue
            ? sitemapLocs.Take(maxSitemapsToVisit.Value)
            : sitemapLocs;
        var toVisit = new Stack<string>(topLevelSitemaps.Reverse());

        while (toVisit.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var currentSitemapUrl = toVisit.Pop();
            var doc = await LoadXmlAsync(http, currentSitemapUrl, ct);
            var locs = GetLocs(doc);

            //There is
            try
            {
                var logsPath = Path.Combine(AppContext.BaseDirectory, "Logs");
                Directory.CreateDirectory(logsPath);
                var locsLogPath = Path.Combine(logsPath, "locs.log");
                await File.AppendAllLinesAsync(locsLogPath, locs, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to write locs to locs.log");
            }


            if (IsSitemapIndex(doc))
            {
                _log.LogInformation("Sitemap processed: Url={Url}, RootType=sitemapindex, LocCount={LocCount}",
                    currentSitemapUrl, locs.Count);
                for (var i = locs.Count - 1; i >= 0; i--)
                {
                    toVisit.Push(locs[i]);
                }

                continue;
            }

            if (IsUrlSet(doc))
            {
                _log.LogInformation("Sitemap processed: Url={Url}, RootType=urlset, LocCount={LocCount}",
                    currentSitemapUrl, locs.Count);
                foreach (var loc in locs)
                {
                    if (results.Count >= maxUrlsLimit)
                    {
                        _log.LogWarning("Reached maxUrls={MaxUrls}, stopping URL collection.", maxUrlsLimit);
                        return results.Take(maxUrlsLimit).ToList();
                    }

                    results.Add(loc);
                }

                continue;
            }

            var root = doc.Root?.Name.LocalName ?? "<null>";
            _log.LogError("Unknown sitemap root. Url={Url}, RootType={RootType}", currentSitemapUrl, root);
            throw new InvalidOperationException($"Unknown sitemap root '{root}' at '{currentSitemapUrl}'.");
        }

        return results.ToList();
    }

    private static SitemapDiscoveryService CreateDefaultDiscoveryService()
    {
        var httpClient = new SitemapHttpClient();
        var validator = new SitemapResponseValidator();
        return new SitemapDiscoveryService(
            new SitemapUrlProvider(NullLogger<SitemapUrlProvider>.Instance),
            httpClient,
            validator,
            NullLogger<SitemapDiscoveryService>.Instance);
    }
}
