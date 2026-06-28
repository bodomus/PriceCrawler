using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;
using VarPrice.Application.UseCases;
using VarPrice.Domain.Constants;
using VarPrice.Domain.Enums;
using VarPrice.Domain.Interfaces;
using VarPrice.Domain.Models;

namespace VarPrice.Web.Tests;

public sealed class RefreshProductCatalogUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_FullSafeRefresh_UpsertsAndDeactivatesMissing()
    {
        var calls = new List<string>();
        var crawler = new FakeCrawlerRunRepository(calls);
        var refresh = new FakeRefreshRepository(calls) { CompleteDelay = TimeSpan.FromMilliseconds(20) };
        var catalog = new FakeProductCatalogRepository(calls)
        {
            ActiveCount = 2,
            Result = new ProductCatalogUpsertResult(2, 1, 1, 0),
            DeactivatedCount = 1
        };
        var sut = CreateUseCase(
            new FakeDiscoveryService(calls, ["https://varus.ua/product-a", "https://varus.ua/product-b"]),
            catalog,
            refresh,
            crawler);

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(RefreshProductCatalogStatus.Ok, result.Status);
        Assert.Equal(42, result.RunId);
        Assert.Equal(1001, result.RefreshId);
        Assert.Equal(2, result.DiscoveredCount);
        Assert.Equal(2, result.AcceptedCount);
        Assert.Equal(1, result.InsertedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(1, result.DeactivatedCount);
        Assert.True(result.DeactivationExecuted);
        Assert.Null(result.DeactivationSkipReason);
        Assert.True(result.StageTimings!.Single(x => x.Stage == CrawlerRunStages.RunFinalization).DurationMs >= 10);
        Assert.Equal(RunStatus.Ok, refresh.LastRunStatus);
        Assert.Equal(
            [
                "crawler_start", "refresh_start", "active_count", "discover", "upsert", "deactivate",
                "refresh_complete", "crawler_finish"
            ],
            calls);
        Assert.Equal(1001, catalog.LastRefreshId);
    }

    [Fact]
    public async Task ExecuteAsync_DiscoveryFails_DoesNotDeactivate()
    {
        var calls = new List<string>();
        var crawler = new FakeCrawlerRunRepository(calls);
        var refresh = new FakeRefreshRepository(calls);
        var catalog = new FakeProductCatalogRepository(calls);
        var sut = CreateUseCase(new ThrowingDiscoveryService(), catalog, refresh, crawler);

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(RefreshProductCatalogStatus.Error, result.Status);
        Assert.Equal("catalog_discovery_failed", result.ErrorCode);
        Assert.Equal(RunStatus.Error, refresh.LastRunStatus);
        Assert.Equal("error", refresh.LastFailStatus);
        Assert.Equal(0, catalog.DeactivateCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_UpsertFails_DoesNotDeactivate()
    {
        var crawler = new FakeCrawlerRunRepository([]);
        var refresh = new FakeRefreshRepository([]);
        var catalog = new FakeProductCatalogRepository([]) { ThrowOnUpsert = true };
        var sut = CreateUseCase(
            new FakeDiscoveryService([], ["https://varus.ua/product-a", "https://varus.ua/product-b"]),
            catalog,
            refresh,
            crawler);

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(RefreshProductCatalogStatus.Error, result.Status);
        Assert.Equal("catalog_upsert_failed", result.ErrorCode);
        Assert.Equal(0, catalog.DeactivateCallCount);
        Assert.Equal("catalog_upsert_failed", refresh.LastErrorCode);
        Assert.Equal([CrawlerRunStages.Discovery], crawler.LastStageTimings.Select(x => x.Stage));
        Assert.Equal([CrawlerRunStages.Discovery], result.StageTimings!.Select(x => x.Stage));
    }

    [Fact]
    public async Task ExecuteAsync_SafetyThresholdFails_DoesNotDeactivateAndFinishesError()
    {
        var crawler = new FakeCrawlerRunRepository([]);
        var refresh = new FakeRefreshRepository([]);
        var catalog = new FakeProductCatalogRepository([])
        {
            ActiveCount = 10,
            Result = new ProductCatalogUpsertResult(1, 0, 1, 0)
        };
        var sut = CreateUseCase(
            new FakeDiscoveryService([], ["https://varus.ua/product-a"]),
            catalog,
            refresh,
            crawler,
            new CrawlerOptions { CatalogMinimumExpectedUrls = 2, CatalogMinimumPreviousRatio = 0.5d });

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(RefreshProductCatalogStatus.Error, result.Status);
        Assert.Equal("catalog_refresh_below_minimum", result.ErrorCode);
        Assert.Equal(0, catalog.DeactivateCallCount);
        Assert.Equal(RunStatus.Error, refresh.LastRunStatus);
    }

    [Fact]
    public async Task ExecuteAsync_ScopedFilterActive_UpsertsButDoesNotDeactivate()
    {
        var catalog = new FakeProductCatalogRepository([])
        {
            Result = new ProductCatalogUpsertResult(1, 1, 0, 0)
        };
        var sut = CreateUseCase(
            new FakeDiscoveryService([], ["https://varus.ua/product-a"]),
            catalog,
            new FakeRefreshRepository([]),
            new FakeCrawlerRunRepository([]),
            new CrawlerOptions { VegetablesUrlContains = "/ovochi", CatalogMinimumExpectedUrls = 1 });

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(RefreshProductCatalogStatus.Ok, result.Status);
        Assert.False(result.DeactivationExecuted);
        Assert.Equal("scoped_filter_active", result.DeactivationSkipReason);
        Assert.Equal(0, catalog.DeactivateCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_DeactivationDisabled_UpsertsWithoutDeactivation()
    {
        var catalog = new FakeProductCatalogRepository([])
        {
            Result = new ProductCatalogUpsertResult(1, 1, 0, 0)
        };
        var sut = CreateUseCase(
            new FakeDiscoveryService([], ["https://varus.ua/product-a"]),
            catalog,
            new FakeRefreshRepository([]),
            new FakeCrawlerRunRepository([]),
            new CrawlerOptions { CatalogDeactivationEnabled = false, CatalogMinimumExpectedUrls = 1 });

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(RefreshProductCatalogStatus.Ok, result.Status);
        Assert.False(result.DeactivationExecuted);
        Assert.Equal("deactivation_disabled", result.DeactivationSkipReason);
        Assert.Equal(0, catalog.DeactivateCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_ReactivatedItems_ReturnsReactivatedCount()
    {
        var catalog = new FakeProductCatalogRepository([])
        {
            Result = new ProductCatalogUpsertResult(2, 0, 1, 1)
        };
        var sut = CreateUseCase(
            new FakeDiscoveryService([], ["https://varus.ua/product-a", "https://varus.ua/product-b"]),
            catalog,
            new FakeRefreshRepository([]),
            new FakeCrawlerRunRepository([]));

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, result.ReactivatedCount);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsCatalogProgress()
    {
        var progress = new CrawlerProgressState();
        var catalog = new FakeProductCatalogRepository([])
        {
            Result = new ProductCatalogUpsertResult(2, 1, 1, 0)
        };
        var sut = CreateUseCase(
            new FakeDiscoveryService([], ["https://varus.ua/product-a", "https://varus.ua/product-b"]),
            catalog,
            new FakeRefreshRepository([]),
            new FakeCrawlerRunRepository([]),
            progressReporter: progress);

        await sut.ExecuteAsync(CancellationToken.None);

        var snapshot = progress.GetSnapshot();
        Assert.Equal(2, snapshot.TotalDiscovered);
        Assert.Equal(1, snapshot.NewProducts);
        Assert.Equal(1, snapshot.UpdatedProducts);
        Assert.Equal("Завершено", snapshot.CurrentStage);
        Assert.Equal(string.Empty, snapshot.CurrentItem);
    }

    [Fact]
    public async Task ExecuteAsync_Cancelled_DoesNotDeactivateAndMarksSessionCancelled()
    {
        using var cts = new CancellationTokenSource();
        var crawler = new FakeCrawlerRunRepository([]);
        var refresh = new FakeRefreshRepository([]);
        var catalog = new FakeProductCatalogRepository([]);
        var sut = CreateUseCase(new CancelledDiscoveryService(cts), catalog, refresh, crawler);

        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.ExecuteAsync(cts.Token));

        Assert.Equal(RunStatus.Error, refresh.LastRunStatus);
        Assert.Equal("cancelled", refresh.LastFailStatus);
        Assert.Equal(0, catalog.DeactivateCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_DeactivationFails_FinishesRefreshWithError()
    {
        var crawler = new FakeCrawlerRunRepository([]);
        var refresh = new FakeRefreshRepository([]);
        var catalog = new FakeProductCatalogRepository([])
        {
            Result = new ProductCatalogUpsertResult(2, 1, 1, 0),
            ThrowOnDeactivate = true
        };
        var sut = CreateUseCase(
            new FakeDiscoveryService([], ["https://varus.ua/product-a", "https://varus.ua/product-b"]),
            catalog,
            refresh,
            crawler);

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(RefreshProductCatalogStatus.Error, result.Status);
        Assert.Equal("catalog_deactivation_failed", result.ErrorCode);
        Assert.Equal("catalog_deactivation_failed", refresh.LastErrorCode);
        Assert.Equal(RunStatus.Error, refresh.LastRunStatus);
    }

    [Fact]
    public async Task ExecuteAsync_CompletionFinalizationFails_ReturnsControlledError()
    {
        var crawler = new FakeCrawlerRunRepository([]);
        var refresh = new FakeRefreshRepository([]) { ThrowOnCompleteWithRun = true };
        var catalog = new FakeProductCatalogRepository([])
        {
            Result = new ProductCatalogUpsertResult(1, 1, 0, 0)
        };
        var sut = CreateUseCase(
            new FakeDiscoveryService([], ["https://varus.ua/product-a"]),
            catalog,
            refresh,
            crawler);

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(RefreshProductCatalogStatus.Error, result.Status);
        Assert.Equal("catalog_refresh_finalize_failed", result.ErrorCode);
        Assert.Equal("catalog_refresh_finalize_failed", refresh.LastErrorCode);
    }

    private static RefreshProductCatalogUseCase CreateUseCase(
        IProductUrlDiscoveryService discovery,
        IProductCatalogRepository catalog,
        IProductCatalogRefreshRepository refresh,
        ICrawlerRunRepository crawler,
        CrawlerOptions? options = null,
        ICrawlerProgressReporter? progressReporter = null) =>
        new(
            discovery,
            catalog,
            refresh,
            crawler,
            Options.Create(options ?? new CrawlerOptions
            {
                CatalogMinimumExpectedUrls = 1,
                CatalogMinimumPreviousRatio = 0.5d
            }),
            progressReporter ?? new CrawlerProgressState(),
            NullLogger<RefreshProductCatalogUseCase>.Instance);

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
        public ProductCatalogUpsertResult Result { get; init; } = new(0, 0, 0, 0);

        public int ActiveCount { get; init; }

        public int DeactivatedCount { get; init; }

        public bool ThrowOnUpsert { get; init; }

        public bool ThrowOnDeactivate { get; init; }

        public int DeactivateCallCount { get; private set; }

        public long LastRefreshId { get; private set; }

        public IReadOnlyCollection<ProductCatalogUpsertItem> LastItems { get; private set; } = [];

        public Task<ProductCatalogUpsertResult> UpsertDiscoveredAsync(
            long refreshId,
            IReadOnlyCollection<ProductCatalogUpsertItem> items,
            CancellationToken ct)
        {
            calls.Add("upsert");
            LastRefreshId = refreshId;
            LastItems = items;
            if (ThrowOnUpsert)
            {
                throw new InvalidOperationException("db down");
            }

            return Task.FromResult(Result.ReceivedCount == 0 && items.Count > 0
                ? new ProductCatalogUpsertResult(items.Count, items.Count, 0, 0)
                : Result);
        }

        public Task<int> GetActiveCountAsync(string source, CancellationToken ct)
        {
            calls.Add("active_count");
            return Task.FromResult(ActiveCount);
        }

        public Task<int> DeactivateMissingAsync(
            string source,
            long currentRefreshId,
            DateTimeOffset notSeenSinceUtc,
            DateTimeOffset deactivatedAtUtc,
            CancellationToken ct)
        {
            calls.Add("deactivate");
            DeactivateCallCount++;
            if (ThrowOnDeactivate)
            {
                throw new InvalidOperationException("deactivate failed");
            }

            return Task.FromResult(DeactivatedCount);
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

        public Task<int> ReleaseReservationsAsync(IReadOnlyCollection<long> catalogItemIds, CancellationToken ct) =>
            Task.FromResult(0);

        public Task<ProductCatalogItem?> GetBySourceAndNormalizedUrlAsync(
            string source,
            string normalizedUrl,
            CancellationToken ct) =>
            Task.FromResult<ProductCatalogItem?>(null);
    }

    private sealed class FakeRefreshRepository(List<string> calls) : IProductCatalogRefreshRepository
    {
        public string? LastFailStatus { get; private set; }

        public string? LastErrorCode { get; private set; }

        public RunStatus LastRunStatus { get; private set; } = RunStatus.Running;

        public bool ThrowOnCompleteWithRun { get; init; }

        public TimeSpan CompleteDelay { get; init; }

        public Task<long> StartAsync(
            string source,
            string discoverySource,
            DateTimeOffset startedAtUtc,
            TimeSpan runningTimeout,
            CancellationToken ct)
        {
            calls.Add("refresh_start");
            return Task.FromResult(1001L);
        }

        public Task CompleteAsync(
            long refreshId,
            ProductCatalogRefreshCompletion completion,
            CancellationToken ct)
        {
            calls.Add("refresh_complete");
            return Task.CompletedTask;
        }

        public async Task CompleteWithRunAsync(
            long refreshId,
            long runId,
            ProductCatalogRefreshCompletion completion,
            string? runNote,
            CancellationToken ct)
        {
            calls.Add("refresh_complete");
            if (CompleteDelay > TimeSpan.Zero)
            {
                await Task.Delay(CompleteDelay, ct);
            }

            if (ThrowOnCompleteWithRun)
            {
                throw new InvalidOperationException("finalize failed");
            }

            LastRunStatus = RunStatus.Ok;
        }

        public Task FailAsync(
            long refreshId,
            string status,
            string errorCode,
            string? errorMessage,
            DateTimeOffset finishedAtUtc,
            CancellationToken ct)
        {
            LastFailStatus = status;
            LastErrorCode = errorCode;
            return Task.CompletedTask;
        }

        public Task FailWithRunAsync(
            long refreshId,
            long runId,
            string status,
            string errorCode,
            string? errorMessage,
            DateTimeOffset finishedAtUtc,
            RunStatus runStatus,
            string? runNote,
            CancellationToken ct)
        {
            LastFailStatus = status;
            LastErrorCode = errorCode;
            LastRunStatus = runStatus;
            return Task.CompletedTask;
        }

        public Task<ProductCatalogRefreshSession?> GetByIdAsync(long refreshId, CancellationToken ct) =>
            Task.FromResult<ProductCatalogRefreshSession?>(null);
    }

    private sealed class FakeCrawlerRunRepository(List<string> calls) : ICrawlerRunRepository
    {
        public RunStatus LastStatus { get; private set; } = RunStatus.Running;
        public IReadOnlyList<CrawlerRunStageTiming> LastStageTimings { get; private set; } = [];

        public Task<long> StartAsync(string source, CancellationToken ct)
        {
            calls.Add("crawler_start");
            return Task.FromResult(42L);
        }

        public Task<long> StartAsync(string runType, string source, string? discoverySource, CancellationToken ct)
            => StartAsync(source, ct);

        public Task FinishAsync(long runId, RunStatus status, string? note, CancellationToken ct)
        {
            calls.Add("crawler_finish");
            LastStatus = status;
            return Task.CompletedTask;
        }

        public Task CompleteAsync(
            long runId,
            RunStatus status,
            CrawlerRunStatistics statistics,
            IReadOnlyCollection<CrawlerRunStageTiming> stageTimings,
            string? note,
            string? errorCode,
            string? errorMessage,
            CancellationToken ct)
        {
            LastStageTimings = stageTimings.ToArray();
            return FinishAsync(runId, status, note, ct);
        }
    }
}
