using Microsoft.Extensions.Logging.Abstractions;

using PriceCrawler.Application.Abstractions;
using PriceCrawler.Application.Models;
using PriceCrawler.Application.UseCases;
using PriceCrawler.Domain.Constants;
using PriceCrawler.Domain.Enums;
using PriceCrawler.Domain.Interfaces;
using PriceCrawler.Domain.Models;
using PriceCrawler.Domain.ValueObjects;

namespace PriceCrawler.Web.Tests;

public sealed class PriceCollectionQueueProcessorProgressTests
{
    [Fact]
    public async Task ProductSuccess_IncrementsOnlyProductCounters()
    {
        var progress = new CrawlerProgressState();
        progress.SetProductQueueTotal(1);
        var queue = FakeQueueRepository.WithItem(QueueItemKind.ProductPage, maxAttempts: 1);
        var sut = CreateProcessor(
            queue,
            progress,
            productResult: ProductExtractResult.Success(Card(), 200, 1, 1));

        await sut.DrainQueueAsync(1, CrawlerOptions(), QueueOptions(maxAttempts: 1), null, CancellationToken.None);

        var snapshot = progress.GetSnapshot();
        Assert.Equal(1, snapshot.ProductProcessed);
        Assert.Equal(1, snapshot.ProductSucceeded);
        Assert.Equal(0, snapshot.ProductFailed);
        Assert.Equal(0, snapshot.ListingProcessed);
        Assert.Equal(0, snapshot.ListingSucceeded);
    }

    [Fact]
    public async Task ProductTerminalFailure_IncrementsOnlyProductFailureCounters()
    {
        var progress = new CrawlerProgressState();
        progress.SetProductQueueTotal(1);
        var queue = FakeQueueRepository.WithItem(QueueItemKind.ProductPage, maxAttempts: 1);
        var sut = CreateProcessor(
            queue,
            progress,
            productResult: ProductExtractResult.Fail(CrawlerErrorCodes.NotFound, 404, "missing", 1, 1, false));

        await sut.DrainQueueAsync(1, CrawlerOptions(), QueueOptions(maxAttempts: 1), null, CancellationToken.None);

        var snapshot = progress.GetSnapshot();
        Assert.Equal(1, snapshot.ProductProcessed);
        Assert.Equal(1, snapshot.ProductFailed);
        Assert.Equal(0, snapshot.ListingProcessed);
        Assert.Equal(0, snapshot.ListingFailed);
    }

    [Fact]
    public async Task ProductRetry_DoesNotIncrementTerminalCounters()
    {
        var progress = new CrawlerProgressState();
        progress.SetProductQueueTotal(1);
        var queue = FakeQueueRepository.WithItem(QueueItemKind.ProductPage, maxAttempts: 2);
        var sut = CreateProcessor(
            queue,
            progress,
            productResult: ProductExtractResult.Fail(CrawlerErrorCodes.Timeout, 504, "timeout", 1, 1, true));

        await sut.DrainQueueAsync(1, CrawlerOptions(), QueueOptions(maxAttempts: 2), null, CancellationToken.None);

        var snapshot = progress.GetSnapshot();
        Assert.Equal(0, snapshot.ProductProcessed);
        Assert.Equal(0, snapshot.ProductSucceeded);
        Assert.Equal(0, snapshot.ProductFailed);
        Assert.Equal(0, snapshot.ListingProcessed);
    }

    [Fact]
    public async Task ListingSuccess_IncrementsListingCountersAndTracksFoundVsEnqueuedLinks()
    {
        var progress = new CrawlerProgressState();
        progress.SetListingQueueTotal(1);
        var queue = FakeQueueRepository.WithItem(QueueItemKind.ListingPage, maxAttempts: 1, enqueueReturn: 0);
        var sut = CreateProcessor(
            queue,
            progress,
            listingResult: ListingExtractionResult.Success(
                "https://example/listing",
                Enumerable.Range(1, 541).Select(i => $"https://example/product/{i}").ToArray(),
                200,
                1,
                1));

        await sut.DrainQueueAsync(1, CrawlerOptions(), QueueOptions(maxAttempts: 1), null, CancellationToken.None);

        var snapshot = progress.GetSnapshot();
        Assert.Equal(1, snapshot.ListingProcessed);
        Assert.Equal(1, snapshot.ListingSucceeded);
        Assert.Equal(0, snapshot.ListingFailed);
        Assert.Equal(0, snapshot.ProductProcessed);
        Assert.Equal(0, snapshot.ProductSucceeded);
        Assert.Equal(541, snapshot.ProductLinksDiscoveredFromListings);
        Assert.Equal(0, snapshot.ProductLinksEnqueuedFromListings);
        Assert.Equal(0, snapshot.ProductQueueTotal);
    }

    [Fact]
    public async Task ListingEnqueue_IncreasesProductQueueTotalByActualEnqueuedCount()
    {
        var progress = new CrawlerProgressState();
        progress.SetListingQueueTotal(1);
        var queue = FakeQueueRepository.WithItem(QueueItemKind.CategoryPage, maxAttempts: 1, enqueueReturn: 3);
        var sut = CreateProcessor(
            queue,
            progress,
            listingResult: ListingExtractionResult.Success(
                "https://example/category",
                Enumerable.Range(1, 5).Select(i => $"https://example/product/{i}").ToArray(),
                200,
                1,
                1));

        await sut.DrainQueueAsync(1, CrawlerOptions(), QueueOptions(maxAttempts: 1), null, CancellationToken.None);

        var snapshot = progress.GetSnapshot();
        Assert.Equal(5, snapshot.ProductLinksDiscoveredFromListings);
        Assert.Equal(3, snapshot.ProductLinksEnqueuedFromListings);
        Assert.Equal(3, snapshot.ProductQueueTotal);
    }

    [Fact]
    public async Task ListingTerminalFailure_IncrementsOnlyListingFailureCounters()
    {
        var progress = new CrawlerProgressState();
        progress.SetListingQueueTotal(1);
        var queue = FakeQueueRepository.WithItem(QueueItemKind.ListingPage, maxAttempts: 1);
        var sut = CreateProcessor(
            queue,
            progress,
            listingResult: ListingExtractionResult.WithIssue(
                "https://example/listing",
                [],
                CrawlerErrorCodes.Http5xx,
                500,
                "server error",
                1,
                1,
                false));

        await sut.DrainQueueAsync(1, CrawlerOptions(), QueueOptions(maxAttempts: 1), null, CancellationToken.None);

        var snapshot = progress.GetSnapshot();
        Assert.Equal(1, snapshot.ListingProcessed);
        Assert.Equal(1, snapshot.ListingFailed);
        Assert.Equal(0, snapshot.ProductProcessed);
        Assert.Equal(0, snapshot.ProductFailed);
    }

    [Fact]
    public async Task ListingRetry_DoesNotIncrementTerminalCounters()
    {
        var progress = new CrawlerProgressState();
        progress.SetListingQueueTotal(1);
        var queue = FakeQueueRepository.WithItem(QueueItemKind.ListingPage, maxAttempts: 2);
        var sut = CreateProcessor(
            queue,
            progress,
            listingResult: ListingExtractionResult.WithIssue(
                "https://example/listing",
                [],
                CrawlerErrorCodes.Timeout,
                504,
                "timeout",
                1,
                1,
                true));

        await sut.DrainQueueAsync(1, CrawlerOptions(), QueueOptions(maxAttempts: 2), null, CancellationToken.None);

        var snapshot = progress.GetSnapshot();
        Assert.Equal(0, snapshot.ListingProcessed);
        Assert.Equal(0, snapshot.ListingSucceeded);
        Assert.Equal(0, snapshot.ListingFailed);
        Assert.Equal(0, snapshot.ProductProcessed);
    }

    private static PriceCollectionQueueProcessor CreateProcessor(
        FakeQueueRepository queue,
        ICrawlerProgressReporter progress,
        ProductExtractResult? productResult = null,
        ListingExtractionResult? listingResult = null)
        => new(
            queue,
            new FakePriceSnapshotRepository(),
            new FakeProductExtractor(productResult ?? ProductExtractResult.Success(Card(), 200, 1, 1)),
            new FakeListingExtractor(listingResult ?? ListingExtractionResult.Success("https://example/listing", [], 200, 1, 1)),
            progress,
            NullLogger<PriceCollectionQueueProcessor>.Instance);

    private static CrawlerOptions CrawlerOptions() => new() { MaxConcurrency = 1 };

    private static QueueOptions QueueOptions(int maxAttempts) => new()
    {
        BatchSize = 10,
        PollDelayMs = 1,
        LeaseSeconds = 10,
        MaxAttempts = maxAttempts,
        RetryBaseDelayMs = 1,
        RetryMaxDelayMs = 1,
        ReaperIntervalSeconds = 1
    };

    private static ProductCard Card() =>
        new("sku", "name", "https://example/product", "slug", 10m, 12m, false, true, null, null);

    private sealed class FakeProductExtractor(ProductExtractResult result) : IProductCardExtractor
    {
        public Task<ProductExtractResult> ExtractAsync(string url, CancellationToken ct) => Task.FromResult(result);
    }

    private sealed class FakeListingExtractor(ListingExtractionResult result) : IListingPageExtractor
    {
        public Task<ListingExtractionResult> ExtractAsync(string url, CancellationToken ct) => Task.FromResult(result);
    }

    private sealed class FakePriceSnapshotRepository : IPriceSnapshotRepository
    {
        public Task<ProductObservationWriteResult> StoreObservationAsync(
            long runId,
            long? queueId,
            ProductObservation observation,
            CancellationToken ct) =>
            Task.FromResult(new ProductObservationWriteResult(1, 1, true));

        public Task<long> InsertCrawlErrorAsync(CrawlErrorRecord error, CancellationToken ct) => Task.FromResult(1L);
    }

    private sealed class FakeQueueRepository : IPriceCollectQueueRepository
    {
        private readonly List<QueueRow> _rows;
        private readonly int _enqueueReturn;

        private FakeQueueRepository(List<QueueRow> rows, int enqueueReturn)
        {
            _rows = rows;
            _enqueueReturn = enqueueReturn;
        }

        public static FakeQueueRepository WithItem(
            QueueItemKind kind,
            int maxAttempts,
            int enqueueReturn = 0) =>
            new(
                [
                    new QueueRow
                    {
                        Id = 1,
                        RunId = 1,
                        Url = kind is QueueItemKind.ListingPage or QueueItemKind.CategoryPage
                            ? "https://example/listing"
                            : "https://example/product",
                        Attempt = 0,
                        MaxAttempts = maxAttempts,
                        PageKind = kind,
                        Status = QueueItemStatuses.Pending
                    }
                ],
                enqueueReturn);

        public Task<int> EnqueueAsync(
            long runId,
            IReadOnlyCollection<QueueEnqueueItem> items,
            int maxAttempts,
            CancellationToken ct) =>
            Task.FromResult(Math.Min(_enqueueReturn, items.Count));

        public Task<IReadOnlyList<ReservedQueueItem>> ReserveBatchAsync(
            long runId,
            int batchSize,
            string workerId,
            TimeSpan leaseDuration,
            CancellationToken ct)
        {
            var reserved = _rows
                .Where(x => x.RunId == runId && x.Status == QueueItemStatuses.Pending)
                .Take(batchSize)
                .ToArray();

            foreach (var row in reserved)
            {
                row.Status = QueueItemStatuses.Reserved;
            }

            return Task.FromResult<IReadOnlyList<ReservedQueueItem>>(reserved
                .Select(x => new ReservedQueueItem(x.Id, x.Url, x.Attempt, x.MaxAttempts, "key", null, x.PageKind))
                .ToArray());
        }

        public Task MarkSucceededAsync(long queueId, CancellationToken ct)
        {
            _rows.Single(x => x.Id == queueId).Status = QueueItemStatuses.Succeeded;
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
            var row = _rows.Single(x => x.Id == queueId);
            row.Attempt++;
            row.Status = QueueItemStatuses.Retry;
            return Task.CompletedTask;
        }

        public Task MarkDeadAsync(long queueId, string errorCode, int? httpStatus, string? message,
            CancellationToken ct)
        {
            var row = _rows.Single(x => x.Id == queueId);
            row.Attempt++;
            row.Status = QueueItemStatuses.Dead;
            return Task.CompletedTask;
        }

        public Task<int> ReapExpiredReservationsAsync(long runId, CancellationToken ct) => Task.FromResult(0);

        public Task<bool> HasOutstandingItemsAsync(long runId, CancellationToken ct) =>
            Task.FromResult(_rows.Any(x => x.RunId == runId && x.Status == QueueItemStatuses.Pending));

        public Task<QueueRunStats> GetRunStatsAsync(long runId, CancellationToken ct) =>
            Task.FromResult(new QueueRunStats(0, 0, 0, 0, 0));

        private sealed class QueueRow
        {
            public long Id { get; init; }
            public long RunId { get; init; }
            public string Url { get; init; } = string.Empty;
            public int Attempt { get; set; }
            public int MaxAttempts { get; init; }
            public QueueItemKind PageKind { get; init; }
            public string Status { get; set; } = QueueItemStatuses.Pending;
        }
    }
}
