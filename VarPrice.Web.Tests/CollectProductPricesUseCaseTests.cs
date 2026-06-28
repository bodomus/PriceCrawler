using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;
using VarPrice.Application.UseCases;
using VarPrice.Domain.Constants;
using VarPrice.Domain.Enums;
using VarPrice.Domain.Interfaces;
using VarPrice.Domain.Models;
using VarPrice.Domain.ValueObjects;

namespace VarPrice.Web.Tests;

public sealed class CollectProductPricesUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_NoDueProducts_CompletesSuccessfullyWithoutQueue()
    {
        var catalog = new FakeProductCatalogRepository([]);
        catalog.Ingestion.FinishDelay = TimeSpan.FromMilliseconds(20);
        var queue = new FakeQueueRepository();
        var sut = CreateUseCase(catalog, queue, ProductExtractResult.Success(Card(), 200, 1, 1));

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal("ok", result.Status);
        Assert.Equal(0, result.SelectedCount);
        Assert.Equal(0, queue.TotalEnqueued);
        Assert.Equal(RunStatus.Ok, catalog.Crawler.LastStatus);
        Assert.True(result.StageTimings!.Single(x => x.Stage == CrawlerRunStages.RunFinalization).DurationMs >= 10);
    }

    [Fact]
    public async Task ExecuteAsync_Success_SelectsEnqueuesProcessesAndMarksCatalogChecked()
    {
        var catalog = new FakeProductCatalogRepository([CatalogItem(1)]);
        var queue = new FakeQueueRepository();
        var sut = CreateUseCase(catalog, queue, ProductExtractResult.Success(Card("sku-new", "slug-new"), 200, 1, 1));

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal("ok", result.Status);
        Assert.Equal(1, result.SelectedCount);
        Assert.Equal(1, result.EnqueuedCount);
        Assert.Equal(1, result.SucceededCount);
        Assert.Single(catalog.Checked);
        Assert.Equal(1, catalog.Checked[0].CatalogItemId);
        Assert.Equal("sku-new", catalog.Checked[0].ExternalId);
        Assert.Equal("slug-new", catalog.Checked[0].Slug);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsPriceCollectionProgress()
    {
        var progress = new CrawlerProgressState();
        var catalog = new FakeProductCatalogRepository([CatalogItem(1), CatalogItem(2)]);
        var queue = new FakeQueueRepository();
        var sut = CreateUseCase(
            catalog,
            queue,
            ProductExtractResult.Success(Card(), 200, 1, 1),
            progressReporter: progress);

        await sut.ExecuteAsync(CancellationToken.None);

        var snapshot = progress.GetSnapshot();
        Assert.Equal(2, snapshot.TotalDiscovered);
        Assert.Equal(2, snapshot.SelectedForCheck);
        Assert.Equal(2, snapshot.NewProducts);
        Assert.Equal(2, snapshot.CheckedProducts);
        Assert.Equal(2, snapshot.SuccessfulProducts);
        Assert.Equal("Завершено", snapshot.CurrentStage);
        Assert.Equal(string.Empty, snapshot.CurrentItem);
    }

    [Theory]
    [InlineData(true, false, 1, 0)]
    [InlineData(false, true, 0, 1)]
    [InlineData(false, false, 0, 0)]
    public async Task ExecuteAsync_ProductWriteFlags_AreCountedExplicitly(
        bool productCreated,
        bool productUpdated,
        int expectedCreated,
        int expectedUpdated)
    {
        var catalog = new FakeProductCatalogRepository([CatalogItem(1)]);
        var queue = new FakeQueueRepository();
        var sut = CreateUseCase(
            catalog,
            queue,
            ProductExtractResult.Success(Card(), 200, 1, 1),
            writeResult: new ProductObservationWriteResult(1, 1, true, productCreated, productUpdated));

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(expectedCreated, result.ProductsCreatedCount);
        Assert.Equal(expectedUpdated, result.ProductsUpdatedCount);
    }

    [Fact]
    public async Task ExecuteAsync_OnItemSucceededThrows_DoesNotMarkQueueItemSucceeded()
    {
        var catalog = new FakeProductCatalogRepository([CatalogItem(1)])
        {
            ThrowOnMarkChecked = true
        };
        var queue = new FakeQueueRepository();
        var sut = CreateUseCase(catalog, queue, ProductExtractResult.Success(Card(), 200, 1, 1));

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Equal(0, queue.CountByStatus(QueueItemStatuses.Succeeded));
        Assert.Equal(1, queue.CountByStatus(QueueItemStatuses.Dead));
    }

    [Fact]
    public async Task ExecuteAsync_FailedCount_IsRetryPlusDead()
    {
        var catalog = new FakeProductCatalogRepository([CatalogItem(1)]);
        var queue = new FakeQueueRepository
        {
            StatsOverride = new QueueRunStats(0, 0, 2, 3, 4)
        };
        var sut = CreateUseCase(catalog, queue, ProductExtractResult.Success(Card(), 200, 1, 1));

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(6, result.FailedCount);
        Assert.Equal(2, result.RetryCount);
        Assert.Equal(4, result.DeadCount);
    }

    [Fact]
    public async Task ExecuteAsync_PartialEnqueue_ReleasesCatalogReservationsNotQueued()
    {
        var catalog = new FakeProductCatalogRepository([CatalogItem(1), CatalogItem(2), CatalogItem(3)]);
        var queue = new FakeQueueRepository { EnqueueLimit = 1 };
        var sut = CreateUseCase(catalog, queue, ProductExtractResult.Success(Card(), 200, 1, 1));

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, result.EnqueuedCount);
        Assert.Equal([2, 3], catalog.ReleasedCatalogItemIds);
    }

    [Fact]
    public async Task ExecuteAsync_EnqueueThrows_ReleasesAllSelectedCatalogReservations()
    {
        var catalog = new FakeProductCatalogRepository([CatalogItem(1), CatalogItem(2)]);
        var queue = new FakeQueueRepository { ThrowOnEnqueue = true };
        var sut = CreateUseCase(catalog, queue, ProductExtractResult.Success(Card(), 200, 1, 1));

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Equal("price_collection_failed", result.ErrorCode);
        Assert.Equal([1, 2], catalog.ReleasedCatalogItemIds);
    }

    [Fact]
    public async Task ExecuteAsync_UsesMaxProductsPerRunAsSelectionLimit()
    {
        var catalog = new FakeProductCatalogRepository([CatalogItem(1), CatalogItem(2), CatalogItem(3)]);
        var queue = new FakeQueueRepository();
        var sut = CreateUseCase(catalog, queue, ProductExtractResult.Success(Card(), 200, 1, 1), maxProducts: 2);

        await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, catalog.LastLimit);
        Assert.Equal(2, queue.TotalEnqueued);
    }

    [Fact]
    public async Task ExecuteAsync_TransientFailureEventuallyDead_MarksCatalogFailedOnce()
    {
        var catalog = new FakeProductCatalogRepository([CatalogItem(1)]);
        var queue = new FakeQueueRepository();
        var sut = CreateUseCase(
            catalog,
            queue,
            ProductExtractResult.Fail(CrawlerErrorCodes.Timeout, 504, "timeout", 1, 1, true),
            maxAttempts: 2);

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Equal(1, result.DeadCount);
        Assert.Single(catalog.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_FinalFailure_MarksCatalogFailedAndFinishesRunsWithError()
    {
        var catalog = new FakeProductCatalogRepository([CatalogItem(1, consecutiveErrors: 1)]);
        var queue = new FakeQueueRepository();
        var sut = CreateUseCase(
            catalog,
            queue,
            ProductExtractResult.Fail(CrawlerErrorCodes.NotFound, 404, "missing", 1, 1, false),
            maxAttempts: 1);

        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Equal(1, result.DeadCount);
        Assert.Single(catalog.Failed);
        Assert.Equal(1, catalog.Failed[0].CatalogItemId);
        Assert.True(catalog.Failed[0].NextCheckAtUtc > catalog.Failed[0].AttemptedAtUtc);
        Assert.Equal(RunStatus.Error, catalog.Crawler.LastStatus);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(1, 120)]
    [InlineData(2, 240)]
    public void CatalogRetryPolicy_ConsecutiveFailures_UsesExponentialDelay(int existingErrors, int expectedMinutes)
    {
        var delay = ProductCatalogRetryPolicy.ComputeDelay(existingErrors, 60, 24);

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), delay);
    }

    [Fact]
    public void CatalogRetryPolicy_LargeFailureCount_DoesNotOverflowAndCaps()
    {
        var delay = ProductCatalogRetryPolicy.ComputeDelay(100, 60, 24);

        Assert.Equal(TimeSpan.FromHours(24), delay);
    }

    private static CollectProductPricesUseCase CreateUseCase(
        FakeProductCatalogRepository catalog,
        FakeQueueRepository queue,
        ProductExtractResult extractResult,
        int maxProducts = 10,
        int maxAttempts = 1,
        ProductObservationWriteResult? writeResult = null,
        ICrawlerProgressReporter? progressReporter = null)
    {
        var progress = progressReporter ?? new CrawlerProgressState();
        var crawlerOptions = Options.Create(new CrawlerOptions
        {
            MaxProductsPerRun = maxProducts,
            MaxConcurrency = 1,
            CatalogLeaseSeconds = 1800,
            SuccessfulCheckIntervalHours = 24,
            CatalogFailureBaseDelayMinutes = 60,
            CatalogFailureMaxDelayHours = 24
        });
        var queueOptions = Options.Create(new QueueOptions
        {
            BatchSize = 10,
            PollDelayMs = 1,
            LeaseSeconds = 10,
            MaxAttempts = maxAttempts,
            RetryBaseDelayMs = 1,
            RetryMaxDelayMs = 10,
            ReaperIntervalSeconds = 1
        });
        var snapshot = new FakePriceSnapshotRepository(writeResult ?? new ProductObservationWriteResult(1, 1, true));
        var processor = new PriceCollectionQueueProcessor(
            queue,
            snapshot,
            new FakeExtractor(extractResult),
            progress,
            NullLogger<PriceCollectionQueueProcessor>.Instance);

        return new CollectProductPricesUseCase(
            crawlerOptions,
            queueOptions,
            catalog,
            catalog.Crawler,
            catalog.Ingestion,
            queue,
            processor,
            progress,
            NullLogger<CollectProductPricesUseCase>.Instance);
    }

    private static ProductCatalogItem CatalogItem(long id, int consecutiveErrors = 0) =>
        new(
            id,
            "varus",
            $"https://example/{id}",
            $"https://example/{id}",
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            null,
            true,
            consecutiveErrors);

    private static ProductCard Card(string externalId = "sku", string slug = "slug") =>
        new(externalId, "name", "https://example/1", slug, 10m, 12m, true, true, null, null);

    private sealed class FakeProductCatalogRepository(IReadOnlyList<ProductCatalogItem> due)
        : IProductCatalogRepository
    {
        public FakeCrawlerRunRepository Crawler { get; } = new();
        public FakeIngestionRunRepository Ingestion { get; } = new();
        public List<ProductCatalogCheckSuccess> Checked { get; } = [];
        public List<ProductCatalogCheckFailure> Failed { get; } = [];
        public List<long> ReleasedCatalogItemIds { get; } = [];
        public bool ThrowOnMarkChecked { get; init; }
        public int LastLimit { get; private set; }

        public Task<ProductCatalogUpsertResult> UpsertDiscoveredAsync(
            long refreshId,
            IReadOnlyCollection<ProductCatalogUpsertItem> items,
            CancellationToken ct) =>
            Task.FromResult(new ProductCatalogUpsertResult(0, 0, 0, 0));

        public Task<int> GetActiveCountAsync(string source, CancellationToken ct) => Task.FromResult(0);

        public Task<int> DeactivateMissingAsync(
            string source,
            long currentRefreshId,
            DateTimeOffset notSeenSinceUtc,
            DateTimeOffset deactivatedAtUtc,
            CancellationToken ct) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<ProductCatalogItem>> GetDueProductsAsync(
            int limit,
            DateTimeOffset nowUtc,
            TimeSpan leaseDuration,
            string workerId,
            CancellationToken ct)
        {
            LastLimit = limit;
            return Task.FromResult<IReadOnlyList<ProductCatalogItem>>(due.Take(limit).ToList());
        }

        public Task MarkCheckedAsync(ProductCatalogCheckSuccess success, CancellationToken ct)
        {
            if (ThrowOnMarkChecked)
            {
                throw new InvalidOperationException("catalog update failed");
            }

            Checked.Add(success);
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(ProductCatalogCheckFailure failure, CancellationToken ct)
        {
            Failed.Add(failure);
            return Task.CompletedTask;
        }

        public Task<ProductCatalogItem?> GetByIdAsync(long id, CancellationToken ct) =>
            Task.FromResult<ProductCatalogItem?>(due.FirstOrDefault(x => x.Id == id));

        public Task<int> ReleaseReservationsAsync(IReadOnlyCollection<long> catalogItemIds, CancellationToken ct)
        {
            ReleasedCatalogItemIds.AddRange(catalogItemIds);
            return Task.FromResult(catalogItemIds.Count);
        }

        public Task<ProductCatalogItem?> GetBySourceAndNormalizedUrlAsync(
            string source,
            string normalizedUrl,
            CancellationToken ct) =>
            Task.FromResult<ProductCatalogItem?>(null);
    }

    private sealed class FakeExtractor(ProductExtractResult result) : IProductCardExtractor
    {
        public Task<ProductExtractResult> ExtractAsync(string url, CancellationToken ct) => Task.FromResult(result);
    }

    private sealed class FakeCrawlerRunRepository : ICrawlerRunRepository
    {
        public RunStatus LastStatus { get; private set; } = RunStatus.Running;
        public CrawlerRunStatistics? LastStatistics { get; private set; }
        public IReadOnlyList<CrawlerRunStageTiming> LastStageTimings { get; private set; } = [];
        public Task<long> StartAsync(string source, CancellationToken ct) => Task.FromResult(1L);

        public Task<long> StartAsync(string runType, string source, string? discoverySource, CancellationToken ct)
            => StartAsync(source, ct);

        public Task FinishAsync(long runId, RunStatus status, string? note, CancellationToken ct)
        {
            LastStatus = status;
            return Task.CompletedTask;
        }

        public Task CompleteAsync(long runId, RunStatus status, CrawlerRunStatistics statistics,
            IReadOnlyCollection<CrawlerRunStageTiming> stageTimings, string? note, string? errorCode,
            string? errorMessage, CancellationToken ct)
        {
            LastStatistics = statistics;
            LastStageTimings = stageTimings.ToArray();
            return FinishAsync(runId, status, note, ct);
        }
    }

    private sealed class FakeIngestionRunRepository : IIngestionRunRepository
    {
        public TimeSpan FinishDelay { get; set; }

        public Task<long> StartAsync(long crawlerRunId, CancellationToken ct) => Task.FromResult(10L);

        public async Task FinishAsync(long ingestionRunId, RunStatus status, ErrorInfo? errorInfo,
            CancellationToken ct)
        {
            if (FinishDelay > TimeSpan.Zero)
            {
                await Task.Delay(FinishDelay, ct);
            }
        }
    }

    private sealed class FakePriceSnapshotRepository(ProductObservationWriteResult writeResult)
        : IPriceSnapshotRepository
    {
        public Task<ProductObservationWriteResult> StoreObservationAsync(
            long runId,
            long? queueId,
            ProductObservation observation,
            CancellationToken ct) =>
            Task.FromResult(writeResult);

        public Task<long> InsertCrawlErrorAsync(CrawlErrorRecord error, CancellationToken ct) => Task.FromResult(1L);
    }

    private sealed class FakeQueueRepository : IPriceCollectQueueRepository
    {
        private readonly Dictionary<long, QueueRow> _rows = [];
        private long _nextId = 1;
        public int TotalEnqueued => _rows.Count;
        public int? EnqueueLimit { get; init; }
        public bool ThrowOnEnqueue { get; init; }
        public QueueRunStats? StatsOverride { get; init; }

        public int CountByStatus(string status) => _rows.Values.Count(x => x.Status == status);

        public Task<int> EnqueueAsync(
            long runId,
            IReadOnlyCollection<QueueEnqueueItem> items,
            int maxAttempts,
            CancellationToken ct)
        {
            if (ThrowOnEnqueue)
            {
                throw new InvalidOperationException("enqueue failed");
            }

            var itemsToInsert = (EnqueueLimit is null
                    ? items
                    : items.Take(EnqueueLimit.Value))
                .ToList();
            foreach (var item in itemsToInsert)
            {
                _rows[_nextId] = new QueueRow
                {
                    Id = _nextId,
                    RunId = runId,
                    Url = item.Url,
                    Status = QueueItemStatuses.Pending,
                    Attempt = 0,
                    MaxAttempts = maxAttempts,
                    ProductCatalogId = item.ProductCatalogId
                };
                _nextId++;
            }

            return Task.FromResult(itemsToInsert.Count);
        }

        public Task<IReadOnlyList<ReservedQueueItem>> ReserveBatchAsync(
            long runId,
            int batchSize,
            string workerId,
            TimeSpan leaseDuration,
            CancellationToken ct)
        {
            var reserved = _rows.Values
                .Where(x => x.RunId == runId &&
                            (x.Status == QueueItemStatuses.Pending || x.Status == QueueItemStatuses.Retry))
                .Take(batchSize)
                .ToList();
            foreach (var row in reserved)
            {
                row.Status = QueueItemStatuses.Reserved;
            }

            return Task.FromResult<IReadOnlyList<ReservedQueueItem>>(reserved
                .Select(x => new ReservedQueueItem(x.Id, x.Url, x.Attempt, x.MaxAttempts, "key", x.ProductCatalogId))
                .ToList());
        }

        public Task MarkSucceededAsync(long queueId, CancellationToken ct)
        {
            _rows[queueId].Status = QueueItemStatuses.Succeeded;
            return Task.CompletedTask;
        }

        public Task MarkRetryAsync(
            long queueId,
            string errorCode,
            int? httpStatus,
            string? message,
            DateTimeOffset nextAttemptAt,
            CancellationToken ct)
        {
            var row = _rows[queueId];
            row.Attempt++;
            row.Status = QueueItemStatuses.Retry;
            return Task.CompletedTask;
        }

        public Task MarkDeadAsync(long queueId, string errorCode, int? httpStatus, string? message,
            CancellationToken ct)
        {
            var row = _rows[queueId];
            row.Attempt++;
            row.Status = QueueItemStatuses.Dead;
            return Task.CompletedTask;
        }

        public Task<int> ReapExpiredReservationsAsync(long runId, CancellationToken ct) => Task.FromResult(0);

        public Task<bool> HasOutstandingItemsAsync(long runId, CancellationToken ct) =>
            Task.FromResult(_rows.Values.Any(x =>
                x.RunId == runId &&
                (x.Status == QueueItemStatuses.Pending ||
                 x.Status == QueueItemStatuses.Reserved ||
                 x.Status == QueueItemStatuses.Retry)));

        public Task<QueueRunStats> GetRunStatsAsync(long runId, CancellationToken ct)
        {
            if (StatsOverride is not null)
            {
                return Task.FromResult(StatsOverride);
            }

            var rows = _rows.Values.Where(x => x.RunId == runId).ToList();
            return Task.FromResult(new QueueRunStats(
                rows.Count(x => x.Status == QueueItemStatuses.Pending),
                rows.Count(x => x.Status == QueueItemStatuses.Reserved),
                rows.Count(x => x.Status == QueueItemStatuses.Retry),
                rows.Count(x => x.Status == QueueItemStatuses.Succeeded),
                rows.Count(x => x.Status == QueueItemStatuses.Dead)));
        }

        private sealed class QueueRow
        {
            public long Id { get; init; }
            public long RunId { get; init; }
            public string Url { get; init; } = string.Empty;
            public string Status { get; set; } = QueueItemStatuses.Pending;
            public int Attempt { get; set; }
            public int MaxAttempts { get; init; }
            public long? ProductCatalogId { get; init; }
        }
    }
}
