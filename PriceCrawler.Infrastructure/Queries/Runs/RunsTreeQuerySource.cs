using Microsoft.EntityFrameworkCore;

using PriceCrawler.Application.Grids.Runs.QueryRows;
using PriceCrawler.Domain.Enums;
using PriceCrawler.Infrastructure.Persistence;

namespace PriceCrawler.Infrastructure.Queries.Runs;

public sealed class RunsTreeQuerySource(PriceCrawlerDbContext dbContext) : IRunsTreeQuerySource
{
    public IQueryable<RunTreeQueryRow> Build()
    {
        return dbContext.CrawlerRuns
            .AsNoTracking()
            .Select(run => new RunTreeQueryRow
            {
                Id = run.Id,
                StartedAtUtc = run.StartedAtUtc,
                FinishedAtUtc = run.FinishedAtUtc,
                Status = run.Status == RunStatus.Running
                    ? "running"
                    : run.Status == RunStatus.Ok
                        ? "ok"
                        : "error",
                ItemsCount = dbContext.PriceSnapshots.Count(snapshot => snapshot.RunId == run.Id),
                SuccessfulSnapshotsCount = dbContext.PriceSnapshots.Count(snapshot =>
                    snapshot.RunId == run.Id
                    && !dbContext.CrawlErrors.Any(error =>
                        error.RunId == snapshot.RunId
                        && error.ProductId == snapshot.ProductId)),
                FailedSnapshotsCount = dbContext.PriceSnapshots.Count(snapshot =>
                    snapshot.RunId == run.Id
                    && dbContext.CrawlErrors.Any(error =>
                        error.RunId == snapshot.RunId
                        && error.ProductId == snapshot.ProductId))
            });
    }
}
