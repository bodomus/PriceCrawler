using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PriceCrawler.Application.Models;
using PriceCrawler.Application.UseCases;
using PriceCrawler.Domain.Constants;
using PriceCrawler.Domain.Enums;
using PriceCrawler.Domain.Interfaces;
using PriceCrawler.Domain.Models;

namespace PriceCrawler.Web.Tests;

public sealed class AbandonedRunCloserTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task CloseAbandonedRuns_ClosesOnlyRunsOlderThanTimeoutWithQueueCounts()
    {
        var now = DateTimeOffset.UtcNow;
        var reads = new FakeRunReadRepository(
            Run(234, CrawlerRunTypes.PriceCollection, now.AddHours(-30), new CrawlerRunStatistics(SelectedCount: 10531)),
            Run(240, CrawlerRunTypes.CatalogRefresh, now.AddHours(-7)),
            Run(241, CrawlerRunTypes.PriceCollection, now.AddHours(-1)));
        var runs = new RecordingRunRepository();
        var queue = new StatsQueueRepository(new QueueRunStats(0, 3, 2, 5600, 7));
        var sut = Create(reads, runs, queue, timeoutHours: 6);

        var closed = await sut.CloseAbandonedRunsAsync(CancellationToken.None);

        Assert.Equal([234L, 240L], closed);
        Assert.Equal([234L, 240L], runs.Completed.Select(x => x.RunId));
        var collect = runs.Completed[0];
        Assert.Equal(RunStatus.Error, collect.Status);
        Assert.Equal(AbandonedRunCloser.ErrorCode, collect.ErrorCode);
        Assert.Equal(10531, collect.Statistics.SelectedCount);
        Assert.Equal(5600, collect.Statistics.SucceededCount);
        Assert.Equal(2, collect.Statistics.RetryCount);
        Assert.Equal(7, collect.Statistics.DeadCount);
        Assert.Equal(9, collect.Statistics.FailedCount);
        Assert.Equal(0, runs.Completed[1].Statistics.SucceededCount);
        Assert.Equal([234L], queue.StatsRequestedFor);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CloseAbandonedRuns_ZeroTimeout_DoesNothing()
    {
        var reads = new FakeRunReadRepository(Run(1, CrawlerRunTypes.PriceCollection, DateTimeOffset.UtcNow.AddDays(-3)));
        var runs = new RecordingRunRepository();
        var sut = Create(reads, runs, new StatsQueueRepository(new QueueRunStats(0, 0, 0, 0, 0)), timeoutHours: 0);

        Assert.Empty(await sut.CloseAbandonedRunsAsync(CancellationToken.None));
        Assert.Empty(runs.Completed);
        Assert.False(reads.Queried);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CloseAbandonedRuns_RepositoryFailure_DoesNotThrow()
    {
        var reads = new FakeRunReadRepository { ThrowOnQuery = true };
        var sut = Create(reads, new RecordingRunRepository(), new StatsQueueRepository(new QueueRunStats(0, 0, 0, 0, 0)), 6);

        Assert.Empty(await sut.CloseAbandonedRunsAsync(CancellationToken.None));
    }

    private static AbandonedRunCloser Create(ICrawlerRunReadRepository reads, ICrawlerRunRepository runs,
        IPriceCollectQueueRepository queue, int timeoutHours) =>
        new(Options.Create(new CrawlerOptions { AbandonedRunTimeoutHours = timeoutHours }), reads, runs, queue,
            NullLogger<AbandonedRunCloser>.Instance);

    private static CrawlerRunDetails Run(long id, string runType, DateTimeOffset startedAt,
        CrawlerRunStatistics? statistics = null) =>
        new(id, runType, runType, null, CrawlerRunStatuses.Running, startedAt, null, null,
            statistics ?? new CrawlerRunStatistics(), [], null, null, null);

    private sealed record CompletedRun(long RunId, RunStatus Status, CrawlerRunStatistics Statistics, string? ErrorCode);

    private sealed class FakeRunReadRepository(params CrawlerRunDetails[] runs) : ICrawlerRunReadRepository
    {
        public bool ThrowOnQuery { get; init; }
        public bool Queried { get; private set; }

        public Task<CrawlerRunDetails?> GetByIdAsync(long runId, CancellationToken ct)
            => Task.FromResult(runs.FirstOrDefault(run => run.Id == runId));

        public Task<IReadOnlyList<CrawlerRunSummary>> GetRecentAsync(int limit, string? runType, string? status,
            CancellationToken ct)
        {
            Queried = true;
            if (ThrowOnQuery) throw new InvalidOperationException("database is down");
            return Task.FromResult<IReadOnlyList<CrawlerRunSummary>>(runs
                .Where(run => status is null || run.Status == status)
                .OrderByDescending(run => run.StartedAtUtc)
                .Select(run => new CrawlerRunSummary(run.Id, run.RunType, run.Source, run.Status, run.StartedAtUtc,
                    null, null, 0, 0, 0, null))
                .ToList());
        }

        public Task<CrawlerRunAggregateStatistics> GetAggregateAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc,
            string? runType, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingRunRepository : ICrawlerRunRepository
    {
        public List<CompletedRun> Completed { get; } = [];

        public Task<long> StartAsync(string source, CancellationToken ct) => throw new NotSupportedException();
        public Task FinishAsync(long runId, RunStatus status, string? note, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> StartAsync(string runType, string source, string? discoverySource, CancellationToken ct) => throw new NotSupportedException();

        public Task CompleteAsync(long runId, RunStatus status, CrawlerRunStatistics statistics,
            IReadOnlyCollection<CrawlerRunStageTiming> stageTimings, string? note, string? errorCode,
            string? errorMessage, CancellationToken ct)
        {
            Completed.Add(new CompletedRun(runId, status, statistics, errorCode));
            return Task.CompletedTask;
        }
    }

    private sealed class StatsQueueRepository(QueueRunStats stats) : IPriceCollectQueueRepository
    {
        public List<long> StatsRequestedFor { get; } = [];

        public Task<QueueRunStats> GetRunStatsAsync(long runId, CancellationToken ct)
        {
            StatsRequestedFor.Add(runId);
            return Task.FromResult(stats);
        }

        public Task<QueueEnqueueResult> EnqueueAsync(long runId, IReadOnlyCollection<QueueEnqueueItem> items, int maxAttempts, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReservedQueueItem>> ReserveBatchAsync(long runId, int batchSize, string workerId, TimeSpan leaseDuration, CancellationToken ct) => throw new NotSupportedException();
        public Task MarkSucceededAsync(long queueId, CancellationToken ct) => throw new NotSupportedException();
        public Task MarkRetryAsync(long queueId, string errorCode, int? httpStatus, string? message, DateTimeOffset nextAttemptAt, CancellationToken ct) => throw new NotSupportedException();
        public Task MarkDeadAsync(long queueId, string errorCode, int? httpStatus, string? message, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> ReapExpiredReservationsAsync(long runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> HasOutstandingItemsAsync(long runId, CancellationToken ct) => throw new NotSupportedException();
    }
}

/// <summary>Builds an <see cref="AbandonedRunCloser"/> that never touches its repositories (timeout 0), for use-case tests.</summary>
internal static class DisabledAbandonedRunCloser
{
    /// <summary>Creates a closer with automatic closing turned off.</summary>
    /// <returns>A closer whose <c>CloseAbandonedRunsAsync</c> returns at once.</returns>
    public static AbandonedRunCloser Create() =>
        new(Options.Create(new CrawlerOptions { AbandonedRunTimeoutHours = 0 }), null!, null!, null!,
            NullLogger<AbandonedRunCloser>.Instance);
}
