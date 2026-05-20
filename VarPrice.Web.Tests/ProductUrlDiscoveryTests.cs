using System.Net;
using System.Text;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using VarPrice.Application.Abstractions;
using VarPrice.Application.DependencyInjection;
using VarPrice.Application.Models;
using VarPrice.Application.UseCases;
using VarPrice.Infrastructure.Crawler;

namespace VarPrice.Web.Tests;

public sealed class ProductUrlDiscoveryTests
{
    [Fact]
    public void CategorySeedUrlsFilePath_ResolvesRelativeToContentRoot()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Crawler:CategorySeedUrlsFilePath"] = "config/category-seed-urls.varus.json"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddCategorySeedUrlFileOptions(configuration, contentRoot);
        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<CategorySeedUrlFileOptions>>()
            .Value;

        Assert.Equal(
            Path.GetFullPath(Path.Combine(contentRoot, "config/category-seed-urls.varus.json")),
            options.ResolvedPath);
    }

    [Fact]
    public async Task CategorySeedSource_ValidatesSeeds_DeduplicatesAndExtractsProducts()
    {
        using var temp = new TempDirectory();
        var seedPath = temp.WriteSeedFile(
            """
            {
              "Crawler": {
                "CategorySeedUrls": [
                  { "name": "Organic", "url": "https://varus.ua/organic-food" },
                  { "name": "Duplicate", "url": "https://varus.ua/organic-food#fragment" },
                  { "name": "", "url": "https://varus.ua/empty-name" },
                  { "name": "Http", "url": "http://varus.ua/http-url" },
                  { "name": "External", "url": "https://example.com/category" },
                  { "name": "Malformed", "url": "not a url" }
                ]
              }
            }
            """);
        using var client = CreateHttpClient(new Dictionary<string, HttpResponseMessage>
        {
            ["https://varus.ua/organic-food"] = Html(
                """
                <html><body>
                  <div class="product-card"><a href="/organic-product-1?tracking=1#top">Product 1</a></div>
                  <div class="product-card"><a href="https://varus.ua/organic-product-1">Product 1 duplicate</a></div>
                  <div class="product-card"><a href="/organic-product-2">Product 2</a></div>
                  <a href="/organic-food">Current category</a>
                </body></html>
                """)
        });

        await using var source = CreateCategorySource(seedPath, client);
        var result = await source.DiscoverProductUrlsAsync(CancellationToken.None);

        Assert.Equal(
            [
                "https://varus.ua/organic-product-1",
                "https://varus.ua/organic-product-2"
            ],
            result.Select(x => x.AbsoluteUri).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task CategorySeedSource_InvalidJson_ReturnsEmptyResult()
    {
        using var temp = new TempDirectory();
        var seedPath = temp.WriteSeedFile("{ invalid json");

        await using var source =
            CreateCategorySource(seedPath, CreateHttpClient(new Dictionary<string, HttpResponseMessage>()));
        var result = await source.DiscoverProductUrlsAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task CategorySeedSource_MissingFile_ReturnsEmptyResult()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.json");

        await using var source =
            CreateCategorySource(missingPath, CreateHttpClient(new Dictionary<string, HttpResponseMessage>()));
        var result = await source.DiscoverProductUrlsAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task CategorySeedSource_CategoryHttpErrors_ContinueWithOtherSeeds()
    {
        using var temp = new TempDirectory();
        var seedPath = temp.WriteSeedFile(
            """
            {
              "Crawler": {
                "CategorySeedUrls": [
                  { "name": "Missing", "url": "https://varus.ua/missing-category" },
                  { "name": "Broken", "url": "https://varus.ua/broken-category" },
                  { "name": "Ok", "url": "https://varus.ua/ok-category" }
                ]
              }
            }
            """);
        using var client = CreateHttpClient(new Dictionary<string, HttpResponseMessage>
        {
            ["https://varus.ua/missing-category"] = new(HttpStatusCode.NotFound),
            ["https://varus.ua/broken-category"] = new(HttpStatusCode.InternalServerError),
            ["https://varus.ua/ok-category"] = Html("<div class=\"product-card\"><a href=\"/ok-product\">Ok</a></div>")
        });

        await using var source = CreateCategorySource(seedPath, client);
        var result = await source.DiscoverProductUrlsAsync(CancellationToken.None);

        Assert.Equal(["https://varus.ua/ok-product"], result.Select(x => x.AbsoluteUri));
    }

    [Fact]
    public void CategoryHtmlParser_DeduplicatesAndNormalizesProductLinks()
    {
        var result = CategoryProductUrlDiscoverySource.ExtractProductUrls(
            """
            <div class="product-card"><a href="/product-a?utm=1#details">A</a></div>
            <div class="product-card"><a href="https://varus.ua/product-a">A duplicate</a></div>
            <div class="product-card"><a href="https://example.com/not-varus">External</a></div>
            <div class="product-card"><a href="/category/subcategory">Nested category</a></div>
            """,
            new Uri("https://varus.ua/category"));

        Assert.Equal(["https://varus.ua/product-a"], result.Select(x => x.AbsoluteUri));
    }

    [Fact]
    public async Task ProductUrlDiscoveryService_SitemapSuccess_SkipsCategoryFallback()
    {
        var sitemap = new FakeDiscoverySource([new Uri("https://varus.ua/from-sitemap")]);
        var category = new FakeDiscoverySource([new Uri("https://varus.ua/from-category")]);
        var service = CreateDiscoveryService(sitemap, category);

        var result = await service.DiscoverProductUrlsAsync(CancellationToken.None);

        Assert.Equal(["https://varus.ua/from-sitemap"], result);
        Assert.Equal(1, sitemap.Calls);
        Assert.Equal(0, category.Calls);
    }

    [Fact]
    public async Task ProductUrlDiscoveryService_SitemapUnavailable_UsesCategoryFallback()
    {
        var sitemap = new FakeDiscoverySource(new SitemapUnavailableException("sitemap down"));
        var category = new FakeDiscoverySource([new Uri("https://varus.ua/from-category")]);
        var service = CreateDiscoveryService(sitemap, category);

        var result = await service.DiscoverProductUrlsAsync(CancellationToken.None);

        Assert.Equal(["https://varus.ua/from-category"], result);
        Assert.Equal(1, category.Calls);
    }

    [Fact]
    public async Task ProductUrlDiscoveryService_EmptySitemap_UsesCategoryFallback()
    {
        var sitemap = new FakeDiscoverySource([]);
        var category = new FakeDiscoverySource([new Uri("https://varus.ua/from-category")]);
        var service = CreateDiscoveryService(sitemap, category);

        var result = await service.DiscoverProductUrlsAsync(CancellationToken.None);

        Assert.Equal(["https://varus.ua/from-category"], result);
        Assert.Equal(1, category.Calls);
    }

    [Fact]
    public async Task ProductUrlDiscoveryService_AllSourcesEmpty_ThrowsControlledFailure()
    {
        var service = CreateDiscoveryService(new FakeDiscoverySource([]), new FakeDiscoverySource([]));

        await Assert.ThrowsAsync<ProductUrlDiscoveryUnavailableException>(() =>
            service.DiscoverProductUrlsAsync(CancellationToken.None));
    }

    private static ProductUrlDiscoveryService CreateDiscoveryService(
        ISitemapProductUrlDiscoverySource sitemap,
        ICategoryProductUrlDiscoverySource category)
        => new(sitemap, category, NullLogger<ProductUrlDiscoveryService>.Instance);

    private static CategoryProductUrlDiscoverySourceHarness CreateCategorySource(string seedPath, HttpClient client)
    {
        var crawlerOptions = Options.Create(new CrawlerOptions
        {
            MaxProductsPerRun = 100,
            MaxUrls = 100,
            RequestsPerSecond = 100d
        });
        var coordinator = new VarusRequestCoordinator(crawlerOptions, NullLogger<VarusRequestCoordinator>.Instance);
        var source = new CategoryProductUrlDiscoverySource(
            new StubHttpClientFactory(client),
            coordinator,
            crawlerOptions,
            Options.Create(new UrlFilterOptions()),
            Options.Create(new CategorySeedUrlFileOptions
            {
                PathSetting = seedPath,
                ResolvedPath = seedPath
            }),
            NullLogger<CategoryProductUrlDiscoverySource>.Instance);

        return new CategoryProductUrlDiscoverySourceHarness(source, coordinator);
    }

    private static HttpClient CreateHttpClient(IReadOnlyDictionary<string, HttpResponseMessage> responses)
        => new(new FakeHttpMessageHandler(responses));

    private static HttpResponseMessage Html(string html)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        };

    private sealed class CategoryProductUrlDiscoverySourceHarness(
        CategoryProductUrlDiscoverySource source,
        VarusRequestCoordinator coordinator) : ICategoryProductUrlDiscoverySource, IAsyncDisposable
    {
        public Task<IReadOnlyCollection<Uri>> DiscoverProductUrlsAsync(CancellationToken ct) =>
            source.DiscoverProductUrlsAsync(ct);

        public async ValueTask DisposeAsync()
        {
            await coordinator.DisposeAsync();
        }
    }

    private sealed class FakeDiscoverySource : ISitemapProductUrlDiscoverySource, ICategoryProductUrlDiscoverySource
    {
        private readonly IReadOnlyCollection<Uri>? _urls;
        private readonly Exception? _exception;

        public FakeDiscoverySource(IReadOnlyCollection<Uri> urls)
        {
            _urls = urls;
        }

        public FakeDiscoverySource(Exception exception)
        {
            _exception = exception;
        }

        public int Calls { get; private set; }

        public Task<IReadOnlyCollection<Uri>> DiscoverProductUrlsAsync(CancellationToken ct)
        {
            Calls++;
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_urls ?? []);
        }
    }

    private sealed class StubHttpClientFactory(HttpClient httpClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => httpClient;
    }

    private sealed class FakeHttpMessageHandler(IReadOnlyDictionary<string, HttpResponseMessage> responses)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var key = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (responses.TryGetValue(key, out var response))
            {
                response.RequestMessage = request;
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request,
                Content = new StringContent($"missing response mapping for {key}")
            });
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public string WriteSeedFile(string json)
        {
            Directory.CreateDirectory(_path);
            var path = Path.Combine(_path, "category-seed-urls.varus.json");
            File.WriteAllText(path, json, Encoding.UTF8);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(_path))
            {
                Directory.Delete(_path, recursive: true);
            }
        }
    }
}
