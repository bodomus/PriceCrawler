using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PriceCrawler.Application.Models;
using PriceCrawler.Domain.Constants;
using PriceCrawler.Domain.Enums;
using PriceCrawler.Domain.Interfaces;

namespace PriceCrawler.Application.UseCases;

/// <summary>
/// Closes crawler runs left in status <c>running</c> by a process that crashed or was killed
/// (power cut, CLR crash, MPC-108). Runs are closed with status <c>error</c> and code <c>run_abandoned</c>;
/// price-collection runs get their real queue counts. Called at the start of <c>collect-prices</c>
/// and <c>catalog-refresh</c>, before the new run is created.
/// </summary>
public sealed class AbandonedRunCloser(
    IOptions<CrawlerOptions> options,
    ICrawlerRunReadRepository runReadRepository,
    ICrawlerRunRepository runRepository,
    IPriceCollectQueueRepository queueRepository,
    ILogger<AbandonedRunCloser> logger)
{
    public const string ErrorCode = "run_abandoned";

    /// <summary>
    /// Finds runs that have been <c>running</c> longer than <see cref="CrawlerOptions.AbandonedRunTimeoutHours"/>
    /// and closes them. Never throws: a failure here must not stop the new run.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ids of the runs that were closed.</returns>
    public async Task<IReadOnlyList<long>> CloseAbandonedRunsAsync(CancellationToken ct)
    {
        var timeoutHours = options.Value.AbandonedRunTimeoutHours;
        if (timeoutHours <= 0)
        {
            return [];
        }

        var closed = new List<long>();
        try
        {
            var boundary = DateTimeOffset.UtcNow.AddHours(-timeoutHours);
            var running = await runReadRepository.GetRecentAsync(200, null, CrawlerRunStatuses.Running, ct);
            foreach (var run in running.Where(run => run.StartedAtUtc < boundary).OrderBy(run => run.Id))
            {
                await CloseAsync(run.Id, timeoutHours, ct);
                closed.Add(run.Id);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not close abandoned crawler runs. Closed so far: {Closed}", closed.Count);
        }

        return closed;
    }

    /// <summary>Closes one abandoned run, keeping its recorded statistics and stages.</summary>
    /// <param name="runId">Run to close.</param>
    /// <param name="timeoutHours">Timeout that made the run abandoned, for the message.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task CloseAsync(long runId, int timeoutHours, CancellationToken ct)
    {
        var details = await runReadRepository.GetByIdAsync(runId, ct);
        if (details is null || details.Status != CrawlerRunStatuses.Running)
        {
            return;
        }

        var statistics = details.Statistics;
        if (details.RunType == CrawlerRunTypes.PriceCollection || details.RunType == CrawlerRunTypes.Legacy)
        {
            var queue = await queueRepository.GetRunStatsAsync(runId, ct);
            statistics = statistics with
            {
                SucceededCount = queue.Succeeded,
                RetryCount = queue.Retry,
                DeadCount = queue.Dead,
                FailedCount = queue.Retry + queue.Dead
            };
        }

        var message =
            $"The run stayed 'running' for more than {timeoutHours} h: its process stopped without finishing it " +
            "(crash, power cut or kill). Closed automatically; finished_at is the closing time, not the crash time.";
        var note = details.Note ?? $"abandoned; succeeded={statistics.SucceededCount}, retry={statistics.RetryCount}, dead={statistics.DeadCount}";
        await runRepository.CompleteAsync(runId, RunStatus.Error, statistics, details.StageTimings, note, ErrorCode,
            message, ct);
        logger.LogWarning(
            "Abandoned crawler run closed. RunId={RunId}; RunType={RunType}; StartedAtUtc={StartedAtUtc}; Succeeded={Succeeded}; Retry={Retry}; Dead={Dead}",
            runId,
            details.RunType,
            details.StartedAtUtc,
            statistics.SucceededCount,
            statistics.RetryCount,
            statistics.DeadCount);
    }
}
