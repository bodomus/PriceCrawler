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
    private const string WorkerCategorySeedUrlsFilePath = "VarPrice.Worker/config/category-seed-urls.varus.json";

    [Fact]
    public void CategorySeedUrlsFilePath_ResolvesRelativeToContentRoot()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Crawler:CategorySeedUrlsFilePath"] = WorkerCategorySeedUrlsFilePath
            })
            .Build();
        var services = new ServiceCollection();

        services.AddCategorySeedUrlFileOptions(configuration, contentRoot);
        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<CategorySeedUrlFileOptions>>()
            .Value;

        Assert.Equal(
            Path.GetFullPath(Path.Combine(contentRoot, WorkerCategorySeedUrlsFilePath)),
            options.ResolvedPath);
    }

    [Fact]
    public void CategorySeedUrlsFilePath_WhenContentRootIsWeb_ResolvesWorkerSeedFile()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var contentRoot = Path.Combine(repoRoot, "VarPrice.Web");
        var expectedSeedPath = Path.Combine(repoRoot, "VarPrice.Worker", "config", "category-seed-urls.varus.json");
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(expectedSeedPath)!);
        File.WriteAllText(expectedSeedPath, "{}");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Crawler:CategorySeedUrlsFilePath"] = WorkerCategorySeedUrlsFilePath
            })
            .Build();
        var services = new ServiceCollection();

        try
        {
            services.AddCategorySeedUrlFileOptions(configuration, contentRoot);
            var options = services.BuildServiceProvider()
                .GetRequiredService<IOptions<CategorySeedUrlFileOptions>>()
                .Value;

            Assert.Equal(WorkerCategorySeedUrlsFilePath, options.PathSetting);
            Assert.Equal(Path.GetFullPath(expectedSeedPath), options.ResolvedPath);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    [Fact]
    public void ProductUrlDiscoveryStrategyFactory_MissingDiscoveryMode_SelectsCategorySeeds()
    {
        using var category = CreateStrategyFactoryCategoryStrategy();
        var factory = CreateStrategyFactory(null, category.Strategy);

        var strategy = factory.Create();

        Assert.Same(category.Strategy, strategy);
    }

    [Fact]
    public void ProductUrlDiscoveryStrategyFactory_CategorySeeds_SelectsCategorySeedStrategy()
    {
        using var category = CreateStrategyFactoryCategoryStrategy();
        var factory = CreateStrategyFactory(ProductUrlDiscoveryModes.CategorySeeds, category.Strategy);

        var strategy = factory.Create();

        Assert.Same(category.Strategy, strategy);
    }

    [Fact]
    public void ProductUrlDiscoveryStrategyFactory_Api_SelectsApiStrategy()
    {
        using var category = CreateStrategyFactoryCategoryStrategy();
        var api = new ApiProductUrlDiscoveryStrategy();
        var factory = CreateStrategyFactory(ProductUrlDiscoveryModes.Api, category.Strategy, api);

        var strategy = factory.Create();

        Assert.Same(api, strategy);
    }

    [Fact]
    public async Task ApiProductUrlDiscoveryStrategy_DiscoverAsync_ThrowsNotSupportedWithClearMessage()
    {
        var strategy = new ApiProductUrlDiscoveryStrategy();

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            strategy.DiscoverAsync(CancellationToken.None));

        Assert.Contains("Crawler:DiscoveryMode=Api", ex.Message);
        Assert.Contains("not implemented yet", ex.Message);
    }

    [Fact]
    public void ProductUrlDiscoveryStrategyFactory_UnsupportedMode_ThrowsClearError()
    {
        using var category = CreateStrategyFactoryCategoryStrategy();
        var factory = CreateStrategyFactory("LegacyFallback", category.Strategy);

        var ex = Assert.Throws<InvalidOperationException>(() => factory.Create());

        Assert.Contains("Unsupported Crawler:DiscoveryMode", ex.Message);
        Assert.Contains("CategorySeeds, Api, Sitemap", ex.Message);
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
    public async Task CategorySeedSource_InvalidJson_ThrowsClearError()
    {
        using var temp = new TempDirectory();
        var seedPath = temp.WriteSeedFile("{ invalid json");

        await using var source =
            CreateCategorySource(seedPath, CreateHttpClient(new Dictionary<string, HttpResponseMessage>()));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.DiscoverProductUrlsAsync(CancellationToken.None));

        Assert.Contains("Invalid JSON in category seed URL file", ex.Message);
    }

    [Fact]
    public async Task CategorySeedSource_MissingFile_ThrowsClearError()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.json");

        await using var source =
            CreateCategorySource(missingPath, CreateHttpClient(new Dictionary<string, HttpResponseMessage>()));
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            source.DiscoverProductUrlsAsync(CancellationToken.None));

        Assert.Contains("Category seed URL file not found", ex.Message);
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
    public async Task CategorySeedSource_FollowsNextPagesUntilNoNewProducts()
    {
        using var temp = new TempDirectory();
        var seedPath = temp.WriteSeedFile(SeedJson("Fresh", "https://varus.ua/ovochi-svizhi"));
        using var client = CreateTrackedHttpClient(new Dictionary<string, HttpResponseMessage>
        {
            ["https://varus.ua/ovochi-svizhi"] = Html(
                """
                <div class="product-card"><a href="/product-page-1">Page 1</a></div>
                <a rel="next" href="/ovochi-svizhi?page=2">Next</a>
                """),
            ["https://varus.ua/ovochi-svizhi?page=2"] = Html(
                """
                <div class="product-card"><a href="/product-page-2">Page 2</a></div>
                <a rel="next" href="/ovochi-svizhi?page=3">Next</a>
                """),
            ["https://varus.ua/ovochi-svizhi?page=3"] = Html(
                """
                <div class="product-card"><a href="/product-page-2">Duplicate</a></div>
                <a rel="next" href="/ovochi-svizhi?page=4">Next</a>
                """)
        }, out var handler);

        await using var source = CreateCategorySource(seedPath, client, maxPagesPerSeed: 5);
        var result = await source.DiscoverProductUrlsAsync(CancellationToken.None);

        Assert.Equal(
            [
                "https://varus.ua/product-page-1",
                "https://varus.ua/product-page-2"
            ],
            result.Select(x => x.AbsoluteUri).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(3, handler.RequestUris.Count);
    }

    [Fact]
    public async Task CategorySeedSource_StopsOnNoNewProductUrls()
    {
        using var temp = new TempDirectory();
        var seedPath = temp.WriteSeedFile(SeedJson("Fresh", "https://varus.ua/ovochi-svizhi"));
        using var client = CreateTrackedHttpClient(new Dictionary<string, HttpResponseMessage>
        {
            ["https://varus.ua/ovochi-svizhi"] = Html(
                """
                <div class="product-card"><a href="/product-page-1">Page 1</a></div>
                <a rel="next" href="/ovochi-svizhi?page=2">Next</a>
                """),
            ["https://varus.ua/ovochi-svizhi?page=2"] = Html(
                """
                <div class="product-card"><a href="/product-page-1?tracking=duplicate">Duplicate</a></div>
                <a rel="next" href="/ovochi-svizhi?page=3">Next</a>
                """),
            ["https://varus.ua/ovochi-svizhi?page=3"] = Html(
                """
                <div class="product-card"><a href="/product-page-3">Should not load</a></div>
                """)
        }, out var handler);

        await using var source = CreateCategorySource(seedPath, client, maxPagesPerSeed: 5);
        var result = await source.DiscoverProductUrlsAsync(CancellationToken.None);

        Assert.Equal(["https://varus.ua/product-page-1"], result.Select(x => x.AbsoluteUri));
        Assert.Equal(
            [
                "https://varus.ua/ovochi-svizhi",
                "https://varus.ua/ovochi-svizhi?page=2"
            ],
            handler.RequestUris);
    }

    [Fact]
    public async Task CategorySeedSource_StopsOnMaxCategoryPagesPerSeed()
    {
        using var temp = new TempDirectory();
        var seedPath = temp.WriteSeedFile(SeedJson("Fresh", "https://varus.ua/ovochi-svizhi"));
        using var client = CreateTrackedHttpClient(new Dictionary<string, HttpResponseMessage>
        {
            ["https://varus.ua/ovochi-svizhi"] = Html(
                """
                <div class="product-card"><a href="/product-page-1">Page 1</a></div>
                <a rel="next" href="/ovochi-svizhi?page=2">Next</a>
                """),
            ["https://varus.ua/ovochi-svizhi?page=2"] = Html(
                """
                <div class="product-card"><a href="/product-page-2">Page 2</a></div>
                <a rel="next" href="/ovochi-svizhi?page=3">Next</a>
                """),
            ["https://varus.ua/ovochi-svizhi?page=3"] = Html(
                """
                <div class="product-card"><a href="/product-page-3">Page 3</a></div>
                """)
        }, out var handler);

        await using var source = CreateCategorySource(seedPath, client, maxPagesPerSeed: 2);
        var result = await source.DiscoverProductUrlsAsync(CancellationToken.None);

        Assert.Equal(
            [
                "https://varus.ua/product-page-1",
                "https://varus.ua/product-page-2"
            ],
            result.Select(x => x.AbsoluteUri).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task CategorySeedSource_StopsOnNoNextPage()
    {
        using var temp = new TempDirectory();
        var seedPath = temp.WriteSeedFile(SeedJson("Fresh", "https://varus.ua/ovochi-svizhi"));
        using var client = CreateTrackedHttpClient(new Dictionary<string, HttpResponseMessage>
        {
            ["https://varus.ua/ovochi-svizhi"] = Html(
                """
                <div class="product-card"><a href="/single-page-product">Single page</a></div>
                """)
        }, out var handler);

        await using var source = CreateCategorySource(seedPath, client, maxPagesPerSeed: 5);
        var result = await source.DiscoverProductUrlsAsync(CancellationToken.None);

        Assert.Equal(["https://varus.ua/single-page-product"], result.Select(x => x.AbsoluteUri));
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task CategorySeedSource_DeduplicatesUrlsAcrossPages()
    {
        using var temp = new TempDirectory();
        var seedPath = temp.WriteSeedFile(SeedJson("Fresh", "https://varus.ua/ovochi-svizhi"));
        using var client = CreateHttpClient(new Dictionary<string, HttpResponseMessage>
        {
            ["https://varus.ua/ovochi-svizhi"] = Html(
                """
                <div class="product-card"><a href="/same-product?utm=1">Same 1</a></div>
                <a rel="next" href="/ovochi-svizhi?page=2">Next</a>
                """),
            ["https://varus.ua/ovochi-svizhi?page=2"] = Html(
                """
                <div class="product-card"><a href="/same-product">Same 2</a></div>
                """)
        });

        await using var source = CreateCategorySource(seedPath, client, maxPagesPerSeed: 5);
        var result = await source.DiscoverProductUrlsAsync(CancellationToken.None);

        Assert.Equal(["https://varus.ua/same-product"], result.Select(x => x.AbsoluteUri));
    }

    [Fact]
    public void CategoryHtmlParser_DeduplicatesAndNormalizesProductLinks()
    {
        var result = new CategoryProductLinkExtractor().ExtractProductUrls(
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
    public async Task ProductUrlDiscoveryService_DefaultCategorySeedDiscovery_DoesNotCallSitemap()
    {
        var category = new FakeDiscoveryStrategy(
            ProductUrlDiscoverySourceKind.CategorySeed,
            "category-seed",
            [new ProductDiscoveryItem("https://varus.ua/from-category")]);
        var sitemap = new FakeDiscoveryStrategy(
            ProductUrlDiscoverySourceKind.Sitemap,
            "sitemap",
            [new ProductDiscoveryItem("https://varus.ua/from-sitemap")]);
        var service = CreateDiscoveryService(category);

        var result = await service.DiscoverProductUrlsAsync(CancellationToken.None);

        Assert.Equal(ProductUrlDiscoverySourceKind.CategorySeed, result.SourceKind);
        Assert.Equal(["https://varus.ua/from-category"], result.Urls);
        Assert.Equal(1, category.Calls);
        Assert.Equal(0, sitemap.Calls);
    }

    [Fact]
    public async Task ProductUrlDiscoveryService_EmptySelectedStrategy_ThrowsControlledFailure()
    {
        var service = CreateDiscoveryService(new FakeDiscoveryStrategy(
            ProductUrlDiscoverySourceKind.CategorySeed,
            "category-seed",
            []));

        await Assert.ThrowsAsync<ProductUrlDiscoveryUnavailableException>(() =>
            service.DiscoverProductUrlsAsync(CancellationToken.None));
    }

    private static ProductUrlDiscoveryService CreateDiscoveryService(IProductUrlDiscoveryStrategy strategy)
        => new(new StaticDiscoveryStrategyFactory(strategy),
            new PassThroughProductUrlFilter(),
            NullLogger<ProductUrlDiscoveryService>.Instance);

    private static CategoryProductUrlDiscoverySourceHarness CreateCategorySource(
        string seedPath,
        HttpClient client,
        int maxPagesPerSeed = 3)
    {
        var crawlerOptions = Options.Create(new CrawlerOptions
        {
            MaxProductsPerRun = 100,
            MaxUrls = 100,
            MaxCategoryPagesPerSeed = maxPagesPerSeed,
            RequestsPerSecond = 100d
        });
        var coordinator = new VarusRequestCoordinator(crawlerOptions, NullLogger<VarusRequestCoordinator>.Instance);
        var source = new CategorySeedProductUrlDiscoveryStrategy(
            new CategorySeedProvider(
                Options.Create(new CategorySeedUrlFileOptions
                {
                    PathSetting = seedPath,
                    ResolvedPath = seedPath
                }),
                NullLogger<CategorySeedProvider>.Instance),
            new CategoryPageLoader(
                new StubHttpClientFactory(client),
                coordinator,
                NullLogger<CategoryPageLoader>.Instance),
            new CategoryProductLinkExtractor(),
            new CategoryPaginationStrategy(),
            crawlerOptions,
            NullLogger<CategorySeedProductUrlDiscoveryStrategy>.Instance);

        return new CategoryProductUrlDiscoverySourceHarness(source, coordinator);
    }

    private static ProductUrlDiscoveryStrategyFactory CreateStrategyFactory(
        string? discoveryMode,
        CategorySeedProductUrlDiscoveryStrategy categoryStrategy,
        ApiProductUrlDiscoveryStrategy? apiStrategy = null)
    {
        var options = Options.Create(new CrawlerOptions
        {
            DiscoveryMode = discoveryMode ?? string.Empty
        });
        var api = apiStrategy ?? new ApiProductUrlDiscoveryStrategy();
        var sitemap = new SitemapProductUrlDiscoveryStrategy(
            options,
            new EmptyProductUrlSource(),
            NullLogger<SitemapProductUrlDiscoveryStrategy>.Instance);

        return new ProductUrlDiscoveryStrategyFactory(options, categoryStrategy, api, sitemap);
    }

    private static CategoryStrategyHarness CreateStrategyFactoryCategoryStrategy()
    {
        var client = CreateHttpClient(new Dictionary<string, HttpResponseMessage>());
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "category-seed-urls.varus.json");
        return new CategoryStrategyHarness(CreateCategorySource(tempPath, client));
    }

    private static HttpClient CreateHttpClient(IReadOnlyDictionary<string, HttpResponseMessage> responses)
        => new(new FakeHttpMessageHandler(responses));

    private static HttpClient CreateTrackedHttpClient(
        IReadOnlyDictionary<string, HttpResponseMessage> responses,
        out FakeHttpMessageHandler handler)
    {
        handler = new FakeHttpMessageHandler(responses);
        return new HttpClient(handler);
    }

    private static HttpResponseMessage Html(string html)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        };

    private static string SeedJson(string name, string url) =>
        $$"""
          {
            "Crawler": {
              "CategorySeedUrls": [
                { "name": "{{name}}", "url": "{{url}}" }
              ]
            }
          }
          """;

    private sealed class CategoryProductUrlDiscoverySourceHarness(
        CategorySeedProductUrlDiscoveryStrategy source,
        VarusRequestCoordinator coordinator) : ICategoryProductUrlDiscoverySource, IAsyncDisposable
    {
        public CategorySeedProductUrlDiscoveryStrategy Strategy => source;

        public Task<IReadOnlyCollection<Uri>> DiscoverProductUrlsAsync(CancellationToken ct) =>
            source.DiscoverProductUrlsAsync(ct);

        public async ValueTask DisposeAsync()
        {
            await coordinator.DisposeAsync();
        }
    }

    private sealed class FakeDiscoveryStrategy(
        ProductUrlDiscoverySourceKind sourceKind,
        string sourceName,
        IReadOnlyCollection<ProductDiscoveryItem> items) : IProductUrlDiscoveryStrategy
    {
        public ProductUrlDiscoverySourceKind SourceKind => sourceKind;

        public string SourceName => sourceName;

        public int Calls { get; private set; }

        public Task<IReadOnlyCollection<ProductDiscoveryItem>> DiscoverAsync(CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(items);
        }
    }

    private sealed class StaticDiscoveryStrategyFactory(IProductUrlDiscoveryStrategy strategy)
        : IProductUrlDiscoveryStrategyFactory
    {
        public IProductUrlDiscoveryStrategy Create() => strategy;
    }

    private sealed class EmptyProductUrlSource : IProductUrlSource
    {
        public Task<IReadOnlyList<string>> GetProductUrlsAsync(string sitemapIndexUrl, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class CategoryStrategyHarness(CategoryProductUrlDiscoverySourceHarness source) : IDisposable
    {
        public CategorySeedProductUrlDiscoveryStrategy Strategy => source.Strategy;

        public void Dispose()
        {
            source.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private sealed class PassThroughProductUrlFilter : IProductUrlFilter
    {
        public IReadOnlyList<string> Apply(IEnumerable<Uri> urls, string sourceName) =>
            urls.Select(x => x.AbsoluteUri).ToList();
    }

    private sealed class StubHttpClientFactory(HttpClient httpClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => httpClient;
    }

    private sealed class FakeHttpMessageHandler(IReadOnlyDictionary<string, HttpResponseMessage> responses)
        : HttpMessageHandler
    {
        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var key = request.RequestUri?.AbsoluteUri ?? string.Empty;
            RequestUris.Add(key);
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
