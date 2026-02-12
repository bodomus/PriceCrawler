using AngleSharp.Dom;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using VarPrice.Web.Crawler;
using VarPrice.Web.Storage;

namespace VarPrice.Web.Tests;

public sealed class CrawlerRunnerTests
{
    [Fact]
    public async Task RunVegetablesAsync_SkipsCategoryPages_AndDoesNotCallExtractor()
    {
        var start = new Uri("https://varus.ua/sitemap-index.xml");
        var responses = new Dictionary<string, string>
        {
            ["https://varus.ua/sitemap-index.xml"] = """
                <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <sitemap><loc>https://varus.ua/sitemap.xml</loc></sitemap>
                </sitemapindex>
                """,
            ["https://varus.ua/sitemap.xml"] = """
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://varus.ua/product/kapusta</loc></url>
                </urlset>
                """,
            ["https://varus.ua/product/kapusta"] = """
                <html>
                  <head><title>Kapusta</title></head>
                  <body>Category-like page</body>
                </html>
                """
        };

        var options = Options.Create(new CrawlerOptions
        {
            SitemapIndexUrl = start,
            MaxProductsPerRun = 5,
            MaxSitemapsToVisit = 10,
            MaxUrlsToCollect = 100
        });

        var http = new VarusHttpClient(new HttpClient(new FakeHttpMessageHandler(responses)));
        var parser = new SitemapParser();
        var sitemapCrawler = new SitemapCrawler(http, parser, options, NullLogger<SitemapCrawler>.Instance);
        var extractor = new CountingExtractor();
        var repo = new FakeCrawlerRepository();
        var detector = new StubPageKindDetector(UrlKind.CategoryPage);

        var runner = new CrawlerRunner(
            options,
            sitemapCrawler,
            new VarusProductUrlFilter(),
            http,
            detector,
            extractor,
            repo,
            NullLogger<CrawlerRunner>.Instance);

        var runResult = await runner.RunVegetablesAsync(CancellationToken.None);

        Assert.Equal(0, extractor.CallCount);
        Assert.Equal(0, runResult.ItemsParsed);
        Assert.Equal(0, runResult.ItemsSaved);
    }

    private sealed class StubPageKindDetector(UrlKind kind) : IPageKindDetector
    {
        public UrlKind Detect(IDocument document) => kind;
    }

    private sealed class CountingExtractor : IProductCardExtractor
    {
        public int CallCount { get; private set; }

        public Task<ProductCard?> ExtractAsync(string url, IDocument document, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult<ProductCard?>(null);
        }
    }

    private sealed class FakeHttpMessageHandler(IReadOnlyDictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (!responses.TryGetValue(key, out var payload))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    RequestMessage = request,
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(payload, Encoding.UTF8, "application/xml")
            });
        }
    }

    private sealed class FakeCrawlerRepository : ICrawlerRepository
    {
        private long _runId = 100;

        public long StartRun(string source) => Interlocked.Increment(ref _runId);

        public Task FinishRunAsync(long runId, string status, string? note, CancellationToken ct) => Task.CompletedTask;

        public Task<long> UpsertProductAsync(string productId, string name, string url, decimal? packValue, string? packUnit, CancellationToken ct)
            => Task.FromResult(1L);

        public Task InsertSnapshotAsync(long runId, long productKey, string? city, decimal price, decimal? oldPrice, bool promoFlag, bool? inStock, CancellationToken ct)
            => Task.CompletedTask;
    }
}
