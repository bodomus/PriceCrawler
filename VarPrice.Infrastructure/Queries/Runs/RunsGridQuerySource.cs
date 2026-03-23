using Microsoft.EntityFrameworkCore;

using VarPrice.Application.Grids.Runs.QueryRows;
using VarPrice.Domain.Enums;
using VarPrice.Infrastructure.Persistence;

namespace VarPrice.Infrastructure.Queries.Runs;

public sealed class RunsGridQuerySource(VarPriceDbContext dbContext) : IRunsGridQuerySource
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
