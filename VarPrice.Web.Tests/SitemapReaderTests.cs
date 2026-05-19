using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using VarPrice.Application.Models;
using VarPrice.Infrastructure.Crawler;

namespace VarPrice.Web.Tests;

public sealed class SitemapReaderTests
{
    [Fact]
    public async Task GetProductUrlsAsync_GzipUrlSet_ReturnsExpectedUrls()
    {
        const string url = "https://example.com/sitemap-products.xml";
        var xml = UrlSetXml(
            "https://example.com/products/1",
            "https://example.com/products/2");
        using var client = CreateHttpClient(new Dictionary<string, HttpResponseMessage>
        {
            [url] = CreateXmlResponse(xml, "gzip")
        });

        var sut = CreateSut(client);
        var result = await sut.GetProductUrlsAsync(url, CancellationToken.None);

        Assert.Equal(
            [
                "https://example.com/products/1",
                "https://example.com/products/2"
            ],
            result);
    }

    [Fact]
    public async Task GetProductUrlsAsync_BrotliUrlSet_ReturnsExpectedUrls()
    {
        const string url = "https://example.com/sitemap-products-br.xml";
        var xml = UrlSetXml("https://example.com/products/10");
        using var client = CreateHttpClient(new Dictionary<string, HttpResponseMessage>
        {
            [url] = CreateXmlResponse(xml, "br")
        });

        var sut = CreateSut(client);
        var result = await sut.GetProductUrlsAsync(url, CancellationToken.None);

        Assert.Equal(["https://example.com/products/10"], result);
    }

    [Fact]
    public async Task GetProductUrlsAsync_SitemapIndexRecursion_ReturnsUrlsFromNestedUrlSets()
    {
        const string root = "https://example.com/sitemap.xml";
        const string nestedIndex = "https://example.com/sitemap-nested.xml";
        const string productsA = "https://example.com/sitemap-products-a.xml";
        const string productsB = "https://example.com/sitemap-products-b.xml";

        using var client = CreateHttpClient(new Dictionary<string, HttpResponseMessage>
        {
            [root] = CreateXmlResponse(SitemapIndexXml(productsA, nestedIndex)),
            [nestedIndex] = CreateXmlResponse(SitemapIndexXml(productsB)),
            [productsA] = CreateXmlResponse(UrlSetXml("https://example.com/products/a")),
            [productsB] =
                CreateXmlResponse(UrlSetXml("https://example.com/products/b", "https://example.com/products/c"))
        });

        var sut = CreateSut(client);
        var result = await sut.GetProductUrlsAsync(root, CancellationToken.None);

        Assert.Equal(
            [
                "https://example.com/products/a",
                "https://example.com/products/b",
                "https://example.com/products/c"
            ],
            result.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetProductUrlsAsync_HtmlResponse_ThrowsInvalidOperationException()
    {
        const string url = "https://example.com/sitemap.xml";
        using var client = CreateHttpClient(new Dictionary<string, HttpResponseMessage>
        {
            [url] = new(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body>blocked</body></html>", Encoding.UTF8, "text/html")
            }
        });

        var sut = CreateSut(client);
        await Assert.ThrowsAsync<SitemapUnavailableException>(() =>
            sut.GetProductUrlsAsync(url, CancellationToken.None));
    }

    [Fact]
    public async Task GetProductUrlsAsync_WhenConfiguredSitemapInvalid_UsesFallbackCandidate()
    {
        const string configuredUrl = "https://example.com/sitemap-index.xml";
        const string fallbackUrl = "https://example.com/sitemap.xml";
        using var client = CreateHttpClient(new Dictionary<string, HttpResponseMessage>
        {
            ["https://example.com/robots.txt"] = new(HttpStatusCode.OK)
            {
                Content = new StringContent("Sitemap: https://example.com/sitemap-index.xml", Encoding.UTF8,
                    "text/plain")
            },
            [configuredUrl] = new(HttpStatusCode.NotFound)
            {
                Content = new StringContent("<!doctype html><html><head><title>404 Not Found</title></head></html>",
                    Encoding.UTF8, "text/html")
            },
            [fallbackUrl] = CreateXmlResponse(UrlSetXml("https://example.com/products/fallback"))
        });

        var sut = CreateSut(client);
        var result = await sut.GetProductUrlsAsync(configuredUrl, CancellationToken.None);

        Assert.Equal(["https://example.com/products/fallback"], result);
    }

    [Fact]
    public async Task SitemapUrlProvider_RobotsSitemapLines_AreAddedAndDeduplicated()
    {
        const string configuredUrl = "https://example.com/sitemap-index.xml";
        using var client = CreateHttpClient(new Dictionary<string, HttpResponseMessage>
        {
            ["https://example.com/robots.txt"] = new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    User-agent: *
                    Sitemap: https://example.com/sitemap-index.xml
                    sitemap: https://example.com/sitemap-products.xml # product sitemap
                    """,
                    Encoding.UTF8,
                    "text/plain")
            }
        });
        var provider = new SitemapUrlProvider(NullLogger<SitemapUrlProvider>.Instance);

        var result = await provider.GetCandidatesAsync(client, configuredUrl, CancellationToken.None);

        Assert.Equal(
            [
                "https://example.com/sitemap-index.xml",
                "https://example.com/sitemap-products.xml",
                "https://example.com/sitemap.xml",
                "https://example.com/sitemap_index.xml"
            ],
            result);
    }

    [Fact]
    public async Task SitemapUrlProvider_WhenRobotsHasNoSitemap_UsesFallbackCandidates()
    {
        const string configuredUrl = "https://example.com/custom.xml";
        using var client = CreateHttpClient(new Dictionary<string, HttpResponseMessage>
        {
            ["https://example.com/robots.txt"] = new(HttpStatusCode.OK)
            {
                Content = new StringContent("User-agent: *", Encoding.UTF8, "text/plain")
            }
        });
        var provider = new SitemapUrlProvider(NullLogger<SitemapUrlProvider>.Instance);

        var result = await provider.GetCandidatesAsync(client, configuredUrl, CancellationToken.None);

        Assert.Equal(
            [
                "https://example.com/custom.xml",
                "https://example.com/sitemap.xml",
                "https://example.com/sitemap_index.xml",
                "https://example.com/sitemap-index.xml"
            ],
            result);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "text/html", "<!doctype html><html><title>404 Not Found</title>",
        SitemapLoadFailureKind.NotFound)]
    [InlineData(HttpStatusCode.Forbidden, "text/html", "forbidden", SitemapLoadFailureKind.Forbidden)]
    [InlineData(HttpStatusCode.OK, "text/html", "<html><title>404 Not Found</title>",
        SitemapLoadFailureKind.HtmlInsteadOfXml)]
    [InlineData(HttpStatusCode.OK, "text/plain", "<urlset></urlset>", SitemapLoadFailureKind.InvalidContentType)]
    [InlineData(HttpStatusCode.OK, "application/xml", "<urlset>", SitemapLoadFailureKind.InvalidXml)]
    [InlineData(HttpStatusCode.OK, "application/xml", "", SitemapLoadFailureKind.EmptyBody)]
    [InlineData(HttpStatusCode.InternalServerError, "text/html", "server error", SitemapLoadFailureKind.ServerError)]
    [InlineData((HttpStatusCode)429, "text/html", "rate limited", SitemapLoadFailureKind.RateLimited)]
    public void SitemapResponseValidator_InvalidResponses_ReturnExpectedFailureKind(
        HttpStatusCode statusCode,
        string contentType,
        string body,
        SitemapLoadFailureKind expected)
    {
        var validator = new SitemapResponseValidator();
        var response = new SitemapHttpResponse(
            "https://example.com/sitemap.xml",
            statusCode,
            contentType,
            string.Empty,
            body,
            body);

        var result = validator.Validate(response);

        Assert.False(result.IsValid);
        Assert.Equal(expected, result.FailureKind);
    }

    [Theory]
    [MemberData(nameof(ValidSitemapXml))]
    public void SitemapResponseValidator_ValidSitemapRoots_AreAccepted(string xml)
    {
        var validator = new SitemapResponseValidator();
        var response = new SitemapHttpResponse(
            "https://example.com/sitemap.xml",
            HttpStatusCode.OK,
            "application/xml",
            string.Empty,
            xml,
            xml);

        var result = validator.Validate(response);

        Assert.True(result.IsValid);
        Assert.Equal(SitemapLoadFailureKind.None, result.FailureKind);
    }

    [Fact]
    public async Task GetProductUrlsAsync_DefaultNamespaceLocs_AreExtracted()
    {
        const string url = "https://example.com/sitemap-ns.xml";
        var xml = UrlSetXml("https://example.com/products/ns-1");
        using var client = CreateHttpClient(new Dictionary<string, HttpResponseMessage>
        {
            [url] = CreateXmlResponse(xml)
        });

        var sut = CreateSut(client);
        var result = await sut.GetProductUrlsAsync(url, CancellationToken.None);

        Assert.Equal(["https://example.com/products/ns-1"], result);
    }

    [Fact]
    public async Task CollectUrlsAsync_DeduplicatesAndStopsAtMaxUrls()
    {
        const string mapA = "https://example.com/map-a.xml";
        const string mapB = "https://example.com/map-b.xml";
        using var client = CreateHttpClient(new Dictionary<string, HttpResponseMessage>
        {
            [mapA] = CreateXmlResponse(UrlSetXml("https://example.com/products/1", "https://example.com/products/1")),
            [mapB] = CreateXmlResponse(UrlSetXml("https://example.com/products/2", "https://example.com/products/3"))
        });

        var sut = CreateSut(client);
        var result = await sut.CollectUrlsAsync(client, [mapA, mapB], CancellationToken.None, maxUrls: 2);

        Assert.Equal(2, result.Count);
        Assert.Contains("https://example.com/products/1", result);
        Assert.Contains("https://example.com/products/2", result);
    }

    private static HttpClient CreateHttpClient(IReadOnlyDictionary<string, HttpResponseMessage> responses)
    {
        var handler = new FakeHttpMessageHandler(responses);
        return new HttpClient(handler);
    }

    private static SitemapReader CreateSut(HttpClient client, params string[] excludedUrlSubstrings)
    {
        var crawlerOptions = Options.Create(new CrawlerOptions());
        var options = Options.Create(new UrlFilterOptions
        {
            ExcludedUrlSubstrings = excludedUrlSubstrings
        });
        return new SitemapReader(new FakeHttpClientFactory(client), crawlerOptions, options,
            NullLogger<SitemapReader>.Instance);
    }

    public static TheoryData<string> ValidSitemapXml()
        => new()
        {
            SitemapIndexXml("https://example.com/sitemap-products.xml"),
            UrlSetXml("https://example.com/products/example")
        };

    private static HttpResponseMessage CreateXmlResponse(string xml, string? contentEncoding = null)
    {
        var bytes = Encoding.UTF8.GetBytes(xml);
        if (string.Equals(contentEncoding, "gzip", StringComparison.OrdinalIgnoreCase))
        {
            bytes = CompressWithGzip(bytes);
        }
        else if (string.Equals(contentEncoding, "br", StringComparison.OrdinalIgnoreCase))
        {
            bytes = CompressWithBrotli(bytes);
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        if (!string.IsNullOrWhiteSpace(contentEncoding))
        {
            response.Content.Headers.ContentEncoding.Add(contentEncoding);
        }

        return response;
    }

    private static byte[] CompressWithGzip(byte[] source)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(source, 0, source.Length);
        }

        return output.ToArray();
    }

    private static byte[] CompressWithBrotli(byte[] source)
    {
        using var output = new MemoryStream();
        using (var br = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            br.Write(source, 0, source.Length);
        }

        return output.ToArray();
    }

    private static string UrlSetXml(params string[] urls) =>
        $$"""
          <?xml version="1.0" encoding="UTF-8"?>
          <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
            {{string.Join(Environment.NewLine, urls.Select(url => $"<url><loc>{url}</loc></url>"))}}
          </urlset>
          """;

    private static string SitemapIndexXml(params string[] sitemapLocs) =>
        $$"""
          <?xml version="1.0" encoding="UTF-8"?>
          <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
            {{string.Join(Environment.NewLine, sitemapLocs.Select(url => $"<sitemap><loc>{url}</loc></sitemap>"))}}
          </sitemapindex>
          """;

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FakeHttpMessageHandler(IReadOnlyDictionary<string, HttpResponseMessage> responses)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var key = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (!responses.TryGetValue(key, out var response))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    RequestMessage = request,
                    Content = new StringContent($"missing response mapping for {key}")
                });
            }

            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
