using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;

namespace VarPrice.Infrastructure.Crawler;

public sealed class SitemapReader(IHttpClientFactory httpClientFactory,
    IOptions<UrlFilterOptions> urlFilterOptions,
    ILogger<SitemapReader> log) : IProductUrlSource
{
    private const int DefaultMaxUrls = 20000;
    // private const int DefaultMaxUrls = 200_000;
    private const int DefaultMaxSitemapsToVisit = 10;

    public async Task<IReadOnlyList<string>> GetProductUrlsAsync(string sitemapIndexUrl, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient("varus");
        var doc = await LoadXmlAsync(http, sitemapIndexUrl, ct);
        var excluded = urlFilterOptions.Value.ExcludedUrlSubstrings;
        if (IsUrlSet(doc))
        {
            var locs = GetLocs(doc);
            log.LogInformation("Sitemap processed: Url={Url}, RootType=urlset, LocCount={LocCount}", sitemapIndexUrl, locs.Count);
            var urls = new HashSet<string>(StringComparer.Ordinal);
            foreach (var url in locs)
            {
                if (urls.Count >= DefaultMaxUrls)
                {
                    log.LogWarning("Reached maxUrls={MaxUrls} while processing {Url}", DefaultMaxUrls, sitemapIndexUrl);
                    break;
                }

                urls.Add(url);
            }

            return urls.ToList();
        }

        if (IsSitemapIndex(doc))
        {
            var sitemapLocs = GetLocs(doc);
            log.LogInformation("Sitemap processed: Url={Url}, RootType=sitemapindex, LocCount={LocCount}", sitemapIndexUrl, sitemapLocs.Count);
            return await CollectUrlsAsync(http, sitemapLocs, ct, DefaultMaxUrls, DefaultMaxSitemapsToVisit);
        }

        var root = doc.Root?.Name.LocalName ?? "<null>";
        throw new InvalidOperationException($"Unknown sitemap root '{root}' at '{sitemapIndexUrl}'.");
    }

    public async Task<XDocument> LoadXmlAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        var contentEncoding = string.Join(",", response.Content.Headers.ContentEncoding);
        await using var rawStream = await response.Content.ReadAsStreamAsync(ct);
        var content = await ReadContentAsync(rawStream, response.Content.Headers.ContentEncoding, ct);
        var bodyPreview = GetPreview(content);

        if (!response.IsSuccessStatusCode)
        {
            log.LogError(
                "Failed to load sitemap XML. Url={Url}, StatusCode={StatusCode}, ContentEncoding={ContentEncoding}, Preview={Preview}",
                url,
                (int)response.StatusCode,
                contentEncoding,
                bodyPreview);
            throw new HttpRequestException(
                $"Failed to load sitemap XML from '{url}'. Status code: {(int)response.StatusCode} ({response.StatusCode}).",
                null,
                response.StatusCode);
        }

        if (LooksLikeHtml(bodyPreview))
        {
            log.LogError(
                "Expected XML but received HTML. Url={Url}, StatusCode={StatusCode}, ContentEncoding={ContentEncoding}, Preview={Preview}",
                url,
                (int)response.StatusCode,
                contentEncoding,
                bodyPreview);
            throw new InvalidOperationException($"Expected XML but received HTML from '{url}'. Preview: {bodyPreview}");
        }

        try
        {
            return XDocument.Parse(content, LoadOptions.None);
        }
        catch (Exception ex)
        {
            log.LogError(
                ex,
                "Failed to parse sitemap XML. Url={Url}, StatusCode={StatusCode}, ContentEncoding={ContentEncoding}, Preview={Preview}",
                url,
                (int)response.StatusCode,
                contentEncoding,
                bodyPreview);
            throw;
        }
    }

    public static bool IsSitemapIndex(XDocument doc) => doc.Root?.Name.LocalName == "sitemapindex";

    public static bool IsUrlSet(XDocument doc) => doc.Root?.Name.LocalName == "urlset";

    public List<string> GetLocs(XDocument doc)
    {
        var excluded = urlFilterOptions.Value.ExcludedUrlSubstrings;
        if (doc.Root is null)
        {
            return [];
        }

        var ns = doc.Root.Name.Namespace;
        return doc
            .Descendants(ns + "loc")
            .Select(x => x.Value.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(a=> !excluded.Any(ex => a.Contains(ex, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public async Task<List<string>> CollectUrlsAsync(
        HttpClient http,
        IEnumerable<string> sitemapLocs,
        CancellationToken ct,
        int maxUrls = DefaultMaxUrls,
        int? maxSitemapsToVisit = null)
    {
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

            if (IsSitemapIndex(doc))
            {
                log.LogInformation("Sitemap processed: Url={Url}, RootType=sitemapindex, LocCount={LocCount}", currentSitemapUrl, locs.Count);
                for (var i = locs.Count - 1; i >= 0; i--)
                {
                    toVisit.Push(locs[i]);
                }

                continue;
            }

            if (IsUrlSet(doc))
            {
                log.LogInformation("Sitemap processed: Url={Url}, RootType=urlset, LocCount={LocCount}", currentSitemapUrl, locs.Count);
                foreach (var loc in locs)
                {
                    if (results.Count >= maxUrls)
                    {
                        log.LogWarning("Reached maxUrls={MaxUrls}, stopping URL collection.", maxUrls);
                        return results.Take(maxUrls).ToList();
                    }

                    results.Add(loc);
                }

                continue;
            }

            var root = doc.Root?.Name.LocalName ?? "<null>";
            log.LogError("Unknown sitemap root. Url={Url}, RootType={RootType}", currentSitemapUrl, root);
            throw new InvalidOperationException($"Unknown sitemap root '{root}' at '{currentSitemapUrl}'.");
        }

        return results.ToList();
    }

    private static bool LooksLikeHtml(string xmlOrHtml)
    {
        var trimmed = xmlOrHtml.TrimStart();
        return trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadContentAsync(Stream stream, ICollection<string> contentEncodings, CancellationToken ct)
    {
        await using var decodedStream = WrapDecodingStream(stream, contentEncodings);
        using var reader = new StreamReader(decodedStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct);
    }

    private static Stream WrapDecodingStream(Stream stream, ICollection<string> contentEncodings)
    {
        var current = stream;
        foreach (var encoding in contentEncodings.Reverse())
        {
            if (string.Equals(encoding, "gzip", StringComparison.OrdinalIgnoreCase))
            {
                current = new GZipStream(current, CompressionMode.Decompress);
                continue;
            }

            if (string.Equals(encoding, "br", StringComparison.OrdinalIgnoreCase))
            {
                current = new BrotliStream(current, CompressionMode.Decompress);
            }
        }

        return current;
    }

    private static string GetPreview(string content) => content.Length <= 200 ? content : content[..200];
}
