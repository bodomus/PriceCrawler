using Microsoft.Extensions.Logging.Abstractions;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;
using VarPrice.Application.UseCases;
using VarPrice.Domain.Enums;
using VarPrice.Domain.Interfaces;
using VarPrice.Domain.Models;

namespace VarPrice.Web.Tests;

public sealed class RefreshProductCatalogUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_Success_UpsertsDiscoveredUrlsAndFinishesRun()
    {
        var calls = new List<string>();
        var crawler = new FakeCrawlerRunRepository(calls);
        var catalog = new FakeProductCatalogRepository(calls)
        {
            Result = new ProductCatalogUpsertResult(2, 1, 1)
        };
        var discovery = new FakeDiscoveryService(calls, ["https://varus.ua/product-a", "https://varus.ua/product-b"]);
        var sut = CreateUseCase(discovery, catalog, crawler);

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(RefreshProductCatalogStatus.Ok, result.Status);
        Assert.Equal(42, result.RunId);
        Assert.Equal(2, result.DiscoveredCount);
        Assert.Equal(2, result.AcceptedCount);
        Assert.Equal(1, result.InsertedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(RunStatus.Ok, crawler.LastStatus);
        Assert.Equal(["start", "discover", "upsert", "finish"], calls);
        Assert.Equal("catalog-refresh", crawler.LastSource);
        Assert.Equal(1, catalog.CallCount);
        Assert.Equal(2, catalog.LastItems.Count);
    }

    [Fact]
    public async Task ExecuteAsync_StartsCrawlerRunBeforeDiscovery()
    {
        var calls = new List<string>();
        var sut = CreateUseCase(
            new FakeDiscoveryService(calls, ["https://varus.ua/product-a"]),
            new FakeProductCatalogRepository(calls),
            new FakeCrawlerRunRepository(calls));

        await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(["start", "discover", "upsert", "finish"], calls);
    }

    [Fact]
    public async Task ExecuteAsync_UsesVarusAsProductCatalogSource()
    {
        var catalog = new FakeProductCatalogRepository([]);
        var sut = CreateUseCase(
            new FakeDiscoveryService([], ["https://varus.ua/product-a"], ProductUrlDiscoverySourceKind.CategorySeed),
            catalog,
            new FakeCrawlerRunRepository([]));

        await sut.ExecuteAsync(CancellationToken.None);

        Assert.All(catalog.LastItems, item => Assert.Equal("varus", item.Source));
    }

    [Theory]
    [InlineData(ProductUrlDiscoverySourceKind.CategorySeed, "category-seed")]
    [InlineData(ProductUrlDiscoverySourceKind.Sitemap, "sitemap")]
    [InlineData(ProductUrlDiscoverySourceKind.Api, "api")]
    public async Task ExecuteAsync_ReturnsActualDiscoverySource(ProductUrlDiscoverySourceKind kind, string expected)
    {
        var sut = CreateUseCase(
            new FakeDiscoveryService([], ["https://varus.ua/product-a"], kind),
            new FakeProductCatalogRepository([]),
            new FakeCrawlerRunRepository([]));

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(expected, result.Source);
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedDiscoverySource_DoesNotMapToSitemap()
    {
        var catalog = new FakeProductCatalogRepository([]);
        var crawler = new FakeCrawlerRunRepository([]);
        var sut = CreateUseCase(
            new FakeDiscoveryService([], ["https://varus.ua/product-a"], (ProductUrlDiscoverySourceKind)999),
            catalog,
            crawler);

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(RefreshProductCatalogStatus.Error, result.Status);
        Assert.Equal("catalog_discovery_source_unsupported", result.ErrorCode);
        Assert.Equal("discovery", result.Source);
        Assert.Equal(0, catalog.CallCount);
        Assert.Equal(RunStatus.Error, crawler.LastStatus);
    }


    [Fact]
    public async Task ExecuteAsync_UsesSameDiscoveredAtForAllItems()
    {
        var catalog = new FakeProductCatalogRepository([]);
        var sut = CreateUseCase(
            new FakeDiscoveryService([], ["https://varus.ua/product-a", "https://varus.ua/product-b"]),
            catalog,
            new FakeCrawlerRunRepository([]));

        await sut.ExecuteAsync(CancellationToken.None);

        Assert.Single(catalog.LastItems.Select(x => x.DiscoveredAtUtc).Distinct());
    }

    [Fact]
    public async Task ExecuteAsync_ExtractsSlugFromProductUrl()
    {
        var catalog = new FakeProductCatalogRepository([]);
        var sut = CreateUseCase(
            new FakeDiscoveryService([], ["https://varus.ua/product-name"]),
            catalog,
            new FakeCrawlerRunRepository([]));

        await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal("product-name", Assert.Single(catalog.LastItems).Slug);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleUrls_CallsRepositoryOnce()
    {
        var catalog = new FakeProductCatalogRepository([]);
        var sut = CreateUseCase(
            new FakeDiscoveryService([], ["https://varus.ua/product-a", "https://varus.ua/product-b"]),
            catalog,
            new FakeCrawlerRunRepository([]));

        await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, catalog.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsRepositoryCounters()
    {
        var catalog = new FakeProductCatalogRepository([])
        {
            Result = new ProductCatalogUpsertResult(100, 10, 90)
        };
        var sut = CreateUseCase(
            new FakeDiscoveryService([],
                Enumerable.Range(1, 100).Select(i => $"https://varus.ua/product-{i}").ToList()),
            catalog,
            new FakeCrawlerRunRepository([]));

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(100, result.AcceptedCount);
        Assert.Equal(10, result.InsertedCount);
        Assert.Equal(90, result.UpdatedCount);
    }

    [Fact]
    public async Task ExecuteAsync_DiscoveryUnavailable_FinishesRunWithError()
    {
        var catalog = new FakeProductCatalogRepository([]);
        var crawler = new FakeCrawlerRunRepository([]);
        var sut = CreateUseCase(new UnavailableDiscoveryService(), catalog, crawler);

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(RefreshProductCatalogStatus.Error, result.Status);
        Assert.Equal(CrawlerErrorCodes.ProductUrlDiscoveryUnavailable, result.ErrorCode);
        Assert.Equal(RunStatus.Error, crawler.LastStatus);
        Assert.Equal(0, catalog.CallCount);
        Assert.Equal(0, result.DiscoveredCount);
        Assert.Equal(0, result.AcceptedCount);
        Assert.Equal(0, result.InsertedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(0, result.SkippedCount);
    }

    [Fact]
    public async Task ExecuteAsync_DiscoveryThrowsUnexpectedException_FinishesRunWithError()
    {
        var crawler = new FakeCrawlerRunRepository([]);
        var sut = CreateUseCase(new ThrowingDiscoveryService(), new FakeProductCatalogRepository([]), crawler);

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(RefreshProductCatalogStatus.Error, result.Status);
        Assert.Equal("catalog_discovery_failed", result.ErrorCode);
        Assert.Equal(RunStatus.Error, crawler.LastStatus);
    }

    [Fact]
    public async Task ExecuteAsync_RepositoryThrows_FinishesRunWithError()
    {
        var crawler = new FakeCrawlerRunRepository([]);
        var catalog = new FakeProductCatalogRepository([]) { ThrowOnUpsert = true };
        var sut = CreateUseCase(
            new FakeDiscoveryService([], ["https://varus.ua/product-a", "https://varus.ua/product-b"]),
            catalog,
            crawler);

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(RefreshProductCatalogStatus.Error, result.Status);
        Assert.Equal("catalog_upsert_failed", result.ErrorCode);
        Assert.Equal(2, result.DiscoveredCount);
        Assert.Equal(2, result.SkippedCount);
        Assert.Equal(RunStatus.Error, crawler.LastStatus);
    }

    [Fact]
    public async Task ExecuteAsync_UpsertSucceedsButFinishRunFails_DoesNotReportUpsertFailure()
    {
        var calls = new List<string>();
        var crawler = new FakeCrawlerRunRepository(calls) { ThrowOnFinish = true };
        var catalog = new FakeProductCatalogRepository(calls)
        {
            Result = new ProductCatalogUpsertResult(2, 1, 1)
        };
        var sut = CreateUseCase(
            new FakeDiscoveryService(calls, ["https://varus.ua/product-a", "https://varus.ua/product-b"]),
            catalog,
            crawler);

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(RefreshProductCatalogStatus.Error, result.Status);
        Assert.Equal("catalog_run_finish_failed", result.ErrorCode);
        Assert.Equal(2, result.DiscoveredCount);
        Assert.Equal(2, result.AcceptedCount);
        Assert.Equal(1, result.InsertedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(1, catalog.CallCount);
        Assert.Equal(1, crawler.FinishCallCount);
        Assert.Equal(["start", "discover", "upsert", "finish"], calls);
    }

    [Fact]
    public async Task ExecuteAsync_Cancelled_RethrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        var crawler = new FakeCrawlerRunRepository([]);
        var sut = CreateUseCase(new CancelledDiscoveryService(cts), new FakeProductCatalogRepository([]), crawler);

        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.ExecuteAsync(cts.Token));

        Assert.Equal(RunStatus.Error, crawler.LastStatus);
    }

    private static RefreshProductCatalogUseCase CreateUseCase(
        IProductUrlDiscoveryService discovery,
        IProductCatalogRepository catalog,
        ICrawlerRunRepository crawler) =>
        new(discovery, catalog, crawler, NullLogger<RefreshProductCatalogUseCase>.Instance);

    private sealed class FakeDiscoveryService(
        List<string> calls,
        IReadOnlyList<string> urls,
        ProductUrlDiscoverySourceKind sourceKind = ProductUrlDiscoverySourceKind.CategorySeed)
        : IProductUrlDiscoveryService
    {
        public Task<ProductUrlDiscoveryResult> DiscoverProductUrlsAsync(CancellationToken ct)
        {
            calls.Add("discover");
            return Task.FromResult(new ProductUrlDiscoveryResult(sourceKind, urls));
        }
    }

    private sealed class UnavailableDiscoveryService : IProductUrlDiscoveryService
    {
        public Task<ProductUrlDiscoveryResult> DiscoverProductUrlsAsync(CancellationToken ct) =>
            throw new ProductUrlDiscoveryUnavailableException("No product URLs found.");
    }

    private sealed class ThrowingDiscoveryService : IProductUrlDiscoveryService
    {
        public Task<ProductUrlDiscoveryResult> DiscoverProductUrlsAsync(CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class CancelledDiscoveryService(CancellationTokenSource cts) : IProductUrlDiscoveryService
    {
        public Task<ProductUrlDiscoveryResult> DiscoverProductUrlsAsync(CancellationToken ct)
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new ProductUrlDiscoveryResult(ProductUrlDiscoverySourceKind.CategorySeed, []));
        }
    }

    private sealed class FakeProductCatalogRepository(List<string> calls) : IProductCatalogRepository
    {
        public ProductCatalogUpsertResult Result { get; init; } = new(0, 0, 0);

        public bool ThrowOnUpsert { get; init; }

        public int CallCount { get; private set; }

        public IReadOnlyCollection<ProductCatalogUpsertItem> LastItems { get; private set; } = [];

        public Task<ProductCatalogUpsertResult> UpsertDiscoveredAsync(
            IReadOnlyCollection<ProductCatalogUpsertItem> items,
            CancellationToken ct)
        {
            calls.Add("upsert");
            CallCount++;
            LastItems = items;
            if (ThrowOnUpsert)
            {
                throw new InvalidOperationException("db down");
            }

            return Task.FromResult(Result.ReceivedCount == 0 && items.Count > 0
                ? new ProductCatalogUpsertResult(items.Count, items.Count, 0)
                : Result);
        }

        public Task<ProductCatalogItem?> GetByIdAsync(long id, CancellationToken ct) =>
            Task.FromResult<ProductCatalogItem?>(null);

        public Task<IReadOnlyList<ProductCatalogItem>> GetDueProductsAsync(
            int limit,
            DateTimeOffset nowUtc,
            TimeSpan leaseDuration,
            string workerId,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ProductCatalogItem>>([]);

        public Task MarkCheckedAsync(ProductCatalogCheckSuccess success, CancellationToken ct) =>
            Task.CompletedTask;

        public Task MarkFailedAsync(ProductCatalogCheckFailure failure, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<ProductCatalogItem?> GetBySourceAndNormalizedUrlAsync(
            string source,
            string normalizedUrl,
            CancellationToken ct) =>
            Task.FromResult<ProductCatalogItem?>(null);
    }

    private sealed class FakeCrawlerRunRepository(List<string> calls) : ICrawlerRunRepository
    {
        public RunStatus LastStatus { get; private set; } = RunStatus.Running;

        public string LastSource { get; private set; } = string.Empty;

        public bool ThrowOnFinish { get; init; }

        public int FinishCallCount { get; private set; }

        public Task<long> StartAsync(string source, CancellationToken ct)
        {
            calls.Add("start");
            LastSource = source;
            return Task.FromResult(42L);
        }

        public Task FinishAsync(long runId, RunStatus status, string? note, CancellationToken ct)
        {
            calls.Add("finish");
            FinishCallCount++;
            if (ThrowOnFinish)
            {
                throw new InvalidOperationException("finish failed");
            }

            LastStatus = status;
            return Task.CompletedTask;
        }
    }
}
