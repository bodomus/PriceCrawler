using Microsoft.EntityFrameworkCore;

using PriceCrawler.Application.Grids.Runs.QueryRows;
using PriceCrawler.Domain.Enums;
using PriceCrawler.Infrastructure.Persistence;

namespace PriceCrawler.Infrastructure.Queries.Runs;

public sealed class RunsGridQuerySource(PriceCrawlerDbContext dbContext) : IRunsGridQuerySource
{
    public IQueryable<RunGridQueryRow> Build()
    {
        var query = dbContext.CrawlerRuns
            .AsNoTracking()
            .Select(run => new RunGridQueryRow
            {
                Id = run.Id,
                StartedAtUtc = run.StartedAtUtc,
                FinishedAtUtc = run.FinishedAtUtc,
                Status = run.Status == RunStatus.Running
                    ? "running"
                    : run.Status == RunStatus.Ok
                        ? "ok"
                        : "error",
                ItemsCount = dbContext.PriceSnapshots.Count(snapshot => snapshot.RunId == run.Id)
            });
        return query;
    }
}
