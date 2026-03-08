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

public sealed class RunCrawlerUseCaseTests
{
    [Fact]
    public async Task RunVegetablesAsync_Success_TransitionsStatuses()
    {
        var crawlerRepo = new FakeCrawlerRunRepository();
        var ingestionRepo = new FakeIngestionRunRepository();
        var queueRepo = new FakeQueueRepository();
        var snapshotRepo = new FakePriceSnapshotRepository();
        var source = new FakeSource(["https://example/ovochi/1"]);
        var extractor = new FakeExtractor(ProductExtractResult.Success(
            new ProductCard("1", "name", "url", 10m, null, false, true, null, null, "kyiv"), 200, 10, 1.0d));

        var sut = CreateUseCase(crawlerRepo, ingestionRepo, queueRepo, snapshotRepo, source, extractor);
        var result = await sut.RunVegetablesAsync(CancellationToken.None);

        Assert.Equal("ok", result.Status);
        Assert.Equal(1, result.ProductsProcessed);
        Assert.Equal(0, result.Errors);
        Assert.Equal(RunStatus.Ok, crawlerRepo.LastStatus);
        Assert.Equal(RunStatus.Ok, ingestionRepo.LastStatus);
        Assert.Null(ingestionRepo.LastError);
    }

    [Fact]
    public async Task RunVegetablesAsync_Failure_SetsErrorInfoInIngestionOnly()
    {
        var crawlerRepo = new FakeCrawlerRunRepository();
        var ingestionRepo = new FakeIngestionRunRepository();
        var queueRepo = new FakeQueueRepository();
        var snapshotRepo = new FakePriceSnapshotRepository();
        var source = new ThrowingSource();
        var extractor =
            new FakeExtractor(ProductExtractResult.Fail(CrawlerErrorCodes.Unknown, null, "boom", 10, 1.0d, false));

        var sut = CreateUseCase(crawlerRepo, ingestionRepo, queueRepo, snapshotRepo, source, extractor);
        var result = await sut.RunVegetablesAsync(CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Equal(RunStatus.Error, crawlerRepo.LastStatus);
        Assert.Equal(RunStatus.Error, ingestionRepo.LastStatus);
        Assert.NotNull(ingestionRepo.LastError);
    }

    [Fact]
    public async Task RunVegetablesAsync_NotFound_MarksItemDeadAndPersistsErrorCode()
    {
        var crawlerRepo = new FakeCrawlerRunRepository();
        var ingestionRepo = new FakeIngestionRunRepository();
        var queueRepo = new FakeQueueRepository();
        var snapshotRepo = new FakePriceSnapshotRepository();
        var source = new FakeSource(["https://example/ovochi/missing"]);
        var extractor =
            new FakeExtractor(ProductExtractResult.Fail(CrawlerErrorCodes.NotFound, 404, "HTTP 404", 11, 1.0d, false));

        var sut = CreateUseCase(crawlerRepo, ingestionRepo, queueRepo, snapshotRepo, source, extractor);
        var result = await sut.RunVegetablesAsync(CancellationToken.None);

        Assert.Equal("ok", result.Status);
        Assert.Equal(0, result.ProductsProcessed);
        Assert.Equal(1, result.Errors);
        Assert.Equal(CrawlerErrorCodes.NotFound, snapshotRepo.LastErrorCode);
        Assert.Equal(404, snapshotRepo.LastHttpStatus);
    }

    private static RunCrawlerUseCase CreateUseCase(
        ICrawlerRunRepository crawler,
        IIngestionRunRepository ingestion,
        IPriceCollectQueueRepository queue,
        IPriceSnapshotRepository snapshot,
        IProductUrlSource source,
        IProductCardExtractor extractor)
    {
        var crawlerOptions = Options.Create(new CrawlerOptions
        {
            SitemapIndexUrl = "https://example/sitemap.xml",
            VegetablesUrlContains = "ovochi",
            MaxProductsPerRun = 2
        });
        var queueOptions = Options.Create(new QueueOptions
        {
            BatchSize = 10,
            PollDelayMs = 1,
            LeaseSeconds = 10,
            MaxAttempts = 2,
            RetryBaseDelayMs = 1,
            RetryMaxDelayMs = 20,
            ReaperIntervalSeconds = 1
        });
        var filterOptions = Options.Create(new UrlFilterOptions());
        return new RunCrawlerUseCase(
            crawlerOptions,
            queueOptions,
            filterOptions,
            source,
            extractor,
            crawler,
            ingestion,
            queue,
            snapshot,
            NullLogger<RunCrawlerUseCase>.Instance);
    }

    private sealed class FakeSource(IReadOnlyList<string> urls) : IProductUrlSource
    {
        public Task<IReadOnlyList<string>> GetProductUrlsAsync(string sitemapIndexUrl, CancellationToken ct) =>
            Task.FromResult(urls);
    }

    private sealed class ThrowingSource : IProductUrlSource
    {
        public Task<IReadOnlyList<string>> GetProductUrlsAsync(string sitemapIndexUrl, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class FakeExtractor(ProductExtractResult result) : IProductCardExtractor
    {
        public Task<ProductExtractResult> ExtractAsync(string url, CancellationToken ct) => Task.FromResult(result);
    }

    private sealed class FakeCrawlerRunRepository : ICrawlerRunRepository
    {
        public RunStatus LastStatus { get; private set; } = RunStatus.Running;
        public Task<long> StartAsync(string source, CancellationToken ct) => Task.FromResult(1L);

        public Task FinishAsync(long runId, RunStatus status, string? note, CancellationToken ct)
        {
            LastStatus = status;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeIngestionRunRepository : IIngestionRunRepository
    {
        public RunStatus LastStatus { get; private set; } = RunStatus.Running;
        public ErrorInfo? LastError { get; private set; }
        public Task<long> StartAsync(long crawlerRunId, CancellationToken ct) => Task.FromResult(10L);

        public Task FinishAsync(long ingestionRunId, RunStatus status, ErrorInfo? errorInfo, CancellationToken ct)
        {
            LastStatus = status;
            LastError = errorInfo;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePriceSnapshotRepository : IPriceSnapshotRepository
    {
        public string? LastErrorCode { get; private set; }
        public int? LastHttpStatus { get; private set; }

        public Task<long> UpsertProductAsync(string productId, string name, string url, decimal? packValue,
            string? packUnit, CancellationToken ct) => Task.FromResult(5L);

        public Task InsertSnapshotAsync(long runId, long productKey, string? city, decimal price, decimal? oldPrice,
            bool promoFlag, bool? inStock, long? queueId, CancellationToken ct) => Task.CompletedTask;

        public Task InsertProductErrorAsync(long runId, long? productKey, string? city, decimal price,
            decimal? oldPrice, bool promoFlag, bool? inStock, CancellationToken ct) => Task.CompletedTask;

        public Task InsertProductErrorAsync(long runId, string url, string errorCode, int? httpStatus, string? message,
            CancellationToken ct)
        {
            LastErrorCode = errorCode;
            LastHttpStatus = httpStatus;
            return Task.CompletedTask;
        }

        public Task InsertProductErrorAsync(long runId, long? queueId, string url, string errorCode, int? httpStatus,
            string? message, CancellationToken ct)
            => InsertProductErrorAsync(runId, url, errorCode, httpStatus, message, ct);
    }

    private sealed class FakeQueueRepository : IPriceCollectQueueRepository
    {
        private readonly Dictionary<long, QueueRow> _rows = [];
        private long _nextId = 1;

        public Task<int> EnqueueAsync(long runId, IReadOnlyCollection<QueueEnqueueItem> items, int maxAttempts,
            CancellationToken ct)
        {
            var added = 0;
            foreach (var item in items)
            {
                if (_rows.Values.Any(x => x.RunId == runId && string.Equals(x.Url, item.Url, StringComparison.Ordinal)))
                {
                    continue;
                }

                _rows[_nextId] = new QueueRow
                {
                    QueueId = _nextId,
                    RunId = runId,
                    Url = item.Url,
                    Attempt = 0,
                    MaxAttempts = Math.Max(1, maxAttempts),
                    Status = QueueItemStatuses.Pending
                };
                _nextId++;
                added++;
            }

            return Task.FromResult(added);
        }

        public Task<IReadOnlyList<ReservedQueueItem>> ReserveBatchAsync(long runId, int batchSize, string workerId,
            TimeSpan leaseDuration, CancellationToken ct)
        {
            var reserved = _rows.Values
                .Where(x => x.RunId == runId &&
                            (x.Status == QueueItemStatuses.Pending || x.Status == QueueItemStatuses.Retry))
                .OrderBy(x => x.QueueId)
                .Take(Math.Max(1, batchSize))
                .ToList();

            foreach (var item in reserved)
            {
                item.Status = QueueItemStatuses.Reserved;
            }

            var mapped = reserved
                .Select(x => new ReservedQueueItem(x.QueueId, x.Url, null, x.Attempt, x.MaxAttempts, "fake"))
                .ToList();
            return Task.FromResult<IReadOnlyList<ReservedQueueItem>>(mapped);
        }

        public Task MarkSucceededAsync(long queueId, CancellationToken ct)
        {
            _rows[queueId].Status = QueueItemStatuses.Succeeded;
            return Task.CompletedTask;
        }

        public Task MarkRetryAsync(long queueId, string errorCode, int? httpStatus, string? message,
            DateTimeOffset nextAttemptAt, CancellationToken ct)
        {
            var item = _rows[queueId];
            item.Attempt++;
            item.Status = QueueItemStatuses.Retry;
            return Task.CompletedTask;
        }

        public Task MarkDeadAsync(long queueId, string errorCode, int? httpStatus, string? message,
            CancellationToken ct)
        {
            var item = _rows[queueId];
            item.Attempt++;
            item.Status = QueueItemStatuses.Dead;
            return Task.CompletedTask;
        }

        public Task<int> ReapExpiredReservationsAsync(long runId, CancellationToken ct) => Task.FromResult(0);

        public Task<bool> HasOutstandingItemsAsync(long runId, CancellationToken ct)
            => Task.FromResult(_rows.Values.Any(x =>
                x.RunId == runId &&
                (x.Status == QueueItemStatuses.Pending ||
                 x.Status == QueueItemStatuses.Reserved ||
                 x.Status == QueueItemStatuses.Retry)));

        public Task<QueueRunStats> GetRunStatsAsync(long runId, CancellationToken ct)
        {
            var rows = _rows.Values.Where(x => x.RunId == runId).ToList();
            var stats = new QueueRunStats(
                rows.Count(x => x.Status == QueueItemStatuses.Pending),
                rows.Count(x => x.Status == QueueItemStatuses.Reserved),
                rows.Count(x => x.Status == QueueItemStatuses.Retry),
                rows.Count(x => x.Status == QueueItemStatuses.Succeeded),
                rows.Count(x => x.Status == QueueItemStatuses.Dead));
            return Task.FromResult(stats);
        }

        private sealed class QueueRow
        {
            public long QueueId { get; init; }
            public long RunId { get; init; }
            public string Url { get; init; } = string.Empty;
            public int Attempt { get; set; }
            public int MaxAttempts { get; init; }
            public string Status { get; set; } = QueueItemStatuses.Pending;
        }
    }
}
