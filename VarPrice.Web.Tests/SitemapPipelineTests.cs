using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using VarPrice.Web.Crawler;
using VarPrice.Web.Storage;

namespace VarPrice.Web.Tests;

public sealed class SitemapPipelineTests
{
    [Fact]
    public void ParseSitemapIndexLocs_WithNamespace_ReturnsNestedSitemaps()
    {
        var parser = new SitemapParser();

        var xml = """
                  <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                    <sitemap><loc>https://varus.ua/sitemap.xml</loc></sitemap>
                    <sitemap><loc>https://varus.ua/sitemap_ru.xml</loc></sitemap>
                  </sitemapindex>
                  """;

        var locs = parser.ParseSitemapIndexLocs(xml);

        Assert.True(locs.Count >= 1);
        Assert.Contains(locs, u => u.AbsoluteUri == "https://varus.ua/sitemap.xml");
    }

    [Fact]
    public async Task SitemapIndex_To_UrlSet_Pipeline_ReturnsPageUrls_AndProductUrls()
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
                  <url><loc>https://varus.ua/kapusta-bilokachanna-mita-2-5-kg</loc></url>
                  <url><loc>https://varus.ua/blog/news-1</loc></url>
                </urlset>
                """
        };

        var crawler = CreateSitemapCrawler(responses);
        var result = await crawler.CollectPageUrlsAsync(start, CancellationToken.None);
        var filter = new VarusProductUrlFilter();

        var productUrls = result.PageUrls.Where(filter.IsProductUrl).ToList();

        Assert.True(result.PageUrls.Count >= 1);
        Assert.True(productUrls.Count >= 1);
    }

    [Fact]
    public async Task RunVegetablesAsync_WithMockedHttp_ReturnsAtLeastOneParsedAndSavedItem()
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
                  <url><loc>https://varus.ua/kapusta-bilokachanna-mita-2-5-kg</loc></url>
                </urlset>
                """,
            ["https://varus.ua/kapusta-bilokachanna-mita-2-5-kg"] = """
                <html>
                  <head><title>Капуста білокачанна</title></head>
                  <body>
                    <h1>Капуста білокачанна</h1>
                    <script type="application/ld+json">
                    {"@context":"https://schema.org","@type":"Product","name":"Test"}
                    </script>
                    Артикул: 12345
                    49,99 грн
                  </body>
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
        var extractor = new VarusProductCardExtractor(NullLogger<VarusProductCardExtractor>.Instance);
        var detector = new PageKindDetector();
        var repo = new FakeCrawlerRepository();
        var ingestionRepo = new FakeIngestionRunRepository();

        var runner = new CrawlerRunner(
            options,
            sitemapCrawler,
            new VarusProductUrlFilter(),
            http,
            detector,
            extractor,
            repo,
            ingestionRepo,
            NullLogger<CrawlerRunner>.Instance);

        var runResult = await runner.RunVegetablesAsync(CancellationToken.None);

        Assert.True(runResult.ProductUrlsDiscovered >= 1);
        Assert.True(runResult.ItemsParsed >= 1);
        Assert.True(runResult.ItemsSaved >= 1);
    }

    private static SitemapCrawler CreateSitemapCrawler(IReadOnlyDictionary<string, string> responses)
    {
        var options = Options.Create(new CrawlerOptions
        {
            SitemapIndexUrl = new Uri("https://varus.ua/sitemap-index.xml"),
            MaxSitemapsToVisit = 20,
            MaxUrlsToCollect = 100
        });

        var http = new VarusHttpClient(new HttpClient(new FakeHttpMessageHandler(responses)));
        return new SitemapCrawler(http, new SitemapParser(), options, NullLogger<SitemapCrawler>.Instance);
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

    private sealed class FakeIngestionRunRepository : IIngestionRunRepository
    {
        private long _ingestionRunId = 1000;

        public long StartIngestion(long crawlerRunId, string source) => Interlocked.Increment(ref _ingestionRunId);

        public Task FinishIngestionAsync(long ingestionRunId, string status, string? note, CancellationToken ct) => Task.CompletedTask;

        public Task FailIngestionAsync(long ingestionRunId, Exception ex, string errorSource, CancellationToken ct) => Task.CompletedTask;
    }
}
