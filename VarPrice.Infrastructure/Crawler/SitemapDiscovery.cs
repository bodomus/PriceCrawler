using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml.Linq;

using Microsoft.Extensions.Logging;

using VarPrice.Application.Models;

namespace VarPrice.Infrastructure.Crawler;

public enum SitemapLoadFailureKind
{
    None,
    NotFound,
    Forbidden,
    RateLimited,
    ServerError,
    InvalidContentType,
    HtmlInsteadOfXml,
    InvalidXml,
    EmptyBody,
    UnexpectedStatusCode
}

public sealed record SitemapHttpResponse(
    string Url,
    HttpStatusCode StatusCode,
    string ContentType,
    string ContentEncoding,
    string BodyPreview,
    string Body);

public sealed record SitemapValidationResult(
    bool IsValid,
    SitemapLoadFailureKind FailureKind,
    XDocument? Document,
    string? Message);

public sealed record SitemapDiscoveryResult(string Url, XDocument Document);

public interface ISitemapUrlProvider
{
    Task<IReadOnlyList<string>> GetCandidatesAsync(HttpClient http, string configuredSitemapUrl, CancellationToken ct);
}

public interface ISitemapHttpClient
{
    Task<SitemapHttpResponse> GetAsync(HttpClient http, string url, CancellationToken ct);
}

public interface ISitemapResponseValidator
{
    SitemapValidationResult Validate(SitemapHttpResponse response);
}

public sealed class SitemapUrlProvider(ILogger<SitemapUrlProvider> log) : ISitemapUrlProvider
{
    public async Task<IReadOnlyList<string>> GetCandidatesAsync(HttpClient http, string configuredSitemapUrl,
        CancellationToken ct)
    {
        var candidates = new List<string>();
        var baseUri = TryCreateAbsoluteUri(configuredSitemapUrl);
        AddCandidate(candidates, configuredSitemapUrl);

        if (baseUri is not null)
        {
            await AddRobotsCandidatesAsync(http, baseUri, candidates, ct);
            AddFallbackCandidates(candidates, baseUri);
        }
        else
        {
            AddFallbackCandidates(candidates, new Uri("https://varus.ua/"));
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task AddRobotsCandidatesAsync(HttpClient http, Uri baseUri, List<string> candidates,
        CancellationToken ct)
    {
        var robotsUri = new Uri(baseUri, "/robots.txt");
        try
        {
            using var response = await http.GetAsync(robotsUri, ct);
            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning(
                    "Failed to load robots.txt. Url={Url}; StatusCode={StatusCode}",
                    robotsUri.AbsoluteUri,
                    (int)response.StatusCode);
                return;
            }

            var robots = await response.Content.ReadAsStringAsync(ct);
            foreach (var candidate in ParseRobotsSitemaps(robots))
            {
                AddCandidate(candidates, candidate);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Failed to load robots.txt. Url={Url}", robotsUri.AbsoluteUri);
        }
    }

    public static IReadOnlyList<string> ParseRobotsSitemaps(string robots)
    {
        return robots
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('#', 2)[0].Trim())
            .Where(line => line.StartsWith("Sitemap:", StringComparison.OrdinalIgnoreCase))
            .Select(line => line["Sitemap:".Length..].Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }

    private static void AddFallbackCandidates(List<string> candidates, Uri baseUri)
    {
        AddCandidate(candidates, new Uri(baseUri, "/sitemap.xml").AbsoluteUri);
        AddCandidate(candidates, new Uri(baseUri, "/sitemap_index.xml").AbsoluteUri);
        AddCandidate(candidates, new Uri(baseUri, "/sitemap-index.xml").AbsoluteUri);
    }

    private static Uri? TryCreateAbsoluteUri(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;

    private static void AddCandidate(List<string> candidates, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            candidates.Add(uri.AbsoluteUri);
        }
    }
}

public sealed class SitemapHttpClient : ISitemapHttpClient
{
    private const int PreviewLength = 512;

    public async Task<SitemapHttpResponse> GetAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        var contentEncoding = string.Join(",", response.Content.Headers.ContentEncoding);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty;
        await using var rawStream = await response.Content.ReadAsStreamAsync(ct);
        var body = await ReadContentAsync(rawStream, response.Content.Headers.ContentEncoding, ct);

        return new SitemapHttpResponse(
            url,
            response.StatusCode,
            contentType,
            contentEncoding,
            GetPreview(body),
            body);
    }

    private static async Task<string> ReadContentAsync(Stream stream, ICollection<string> contentEncodings,
        CancellationToken ct)
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

    private static string GetPreview(string content) =>
        content.Length <= PreviewLength ? content : content[..PreviewLength];
}

public sealed class SitemapResponseValidator : ISitemapResponseValidator
{
    public SitemapValidationResult Validate(SitemapHttpResponse response)
    {
        var statusCode = (int)response.StatusCode;
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Rejected(SitemapLoadFailureKind.NotFound, "Sitemap returned 404.");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return Rejected(SitemapLoadFailureKind.Forbidden, "Sitemap returned 403.");
        }

        if (statusCode == 429)
        {
            return Rejected(SitemapLoadFailureKind.RateLimited, "Sitemap returned 429.");
        }

        if (statusCode >= 500)
        {
            return Rejected(SitemapLoadFailureKind.ServerError, "Sitemap returned 5xx.");
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return Rejected(SitemapLoadFailureKind.UnexpectedStatusCode, "Sitemap returned an unexpected status code.");
        }

        if (string.IsNullOrWhiteSpace(response.Body))
        {
            return Rejected(SitemapLoadFailureKind.EmptyBody, "Sitemap body is empty.");
        }

        if (LooksLikeHtml(response.ContentType, response.BodyPreview))
        {
            return Rejected(SitemapLoadFailureKind.HtmlInsteadOfXml, "Sitemap response is HTML instead of XML.");
        }

        if (!IsXmlContentType(response.ContentType))
        {
            return Rejected(SitemapLoadFailureKind.InvalidContentType, "Sitemap content type is not XML.");
        }

        try
        {
            var doc = XDocument.Parse(response.Body, LoadOptions.None);
            var root = doc.Root?.Name.LocalName;
            if (root is "sitemapindex" or "urlset")
            {
                return new SitemapValidationResult(true, SitemapLoadFailureKind.None, doc, null);
            }

            return Rejected(SitemapLoadFailureKind.InvalidXml, $"Unexpected sitemap root '{root ?? "<null>"}'.");
        }
        catch (Exception ex)
        {
            return Rejected(SitemapLoadFailureKind.InvalidXml, ex.Message);
        }
    }

    private static SitemapValidationResult Rejected(SitemapLoadFailureKind kind, string message)
        => new(false, kind, null, message);

    private static bool IsXmlContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return contentType.Contains("xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeHtml(string contentType, string bodyPreview)
    {
        if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var trimmed = bodyPreview.TrimStart();
        return trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
               || trimmed.Contains("<title>404 Not Found</title>", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class SitemapDiscoveryService(
    ISitemapUrlProvider urlProvider,
    ISitemapHttpClient httpClient,
    ISitemapResponseValidator validator,
    ILogger<SitemapDiscoveryService> log)
{
    public async Task<SitemapDiscoveryResult> DiscoverAsync(HttpClient http, string configuredSitemapUrl,
        CancellationToken ct)
    {
        var candidates = await urlProvider.GetCandidatesAsync(http, configuredSitemapUrl, ct);
        var failures = new List<(string Url, SitemapLoadFailureKind Kind)>();

        foreach (var candidate in candidates)
        {
            var response = await httpClient.GetAsync(http, candidate, ct);
            var validation = validator.Validate(response);
            if (validation.IsValid && validation.Document is not null)
            {
                return new SitemapDiscoveryResult(candidate, validation.Document);
            }

            failures.Add((candidate, validation.FailureKind));
            log.LogWarning(
                "Sitemap candidate rejected. Url={Url}; StatusCode={StatusCode}; FailureKind={FailureKind}; ContentType={ContentType}; ContentEncoding={ContentEncoding}; Preview={Preview}",
                response.Url,
                (int)response.StatusCode,
                validation.FailureKind,
                response.ContentType,
                response.ContentEncoding,
                response.BodyPreview);
        }

        var triedUrls = string.Join(", ", failures.Select(x => x.Url));
        var failureKinds = string.Join(", ", failures.Select(x => x.Kind).Distinct());
        log.LogError(
            "Sitemap discovery failed. No valid sitemap found. TriedUrls={TriedUrls}; FailureKinds={FailureKinds}",
            triedUrls,
            failureKinds);

        throw new SitemapUnavailableException(
            $"SitemapUnavailable: No valid sitemap found. TriedUrls={triedUrls}; FailureKinds={failureKinds}");
    }
}
