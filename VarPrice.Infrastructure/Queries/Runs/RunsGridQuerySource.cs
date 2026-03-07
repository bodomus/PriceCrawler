using Microsoft.EntityFrameworkCore;

using VarPrice.Application.Grids.Runs.QueryRows;
using VarPrice.Infrastructure.Persistence;

namespace VarPrice.Infrastructure.Queries.Runs;

public sealed class RunsGridQuerySource(VarPriceDbContext dbContext) : IRunsGridQuerySource
{
    public IQueryable<RunGridQueryRow> Build()
    {
        return dbContext.CrawlerRuns
            .AsNoTracking()
            .Select(run => new RunGridQueryRow
            {
                Id = run.Id,
                StartedAtUtc = run.StartedAtUtc,
                FinishedAtUtc = run.FinishedAtUtc,
                Status = run.Status,
                ItemsCount = dbContext.PriceSnapshots.Count(snapshot => snapshot.RunId == run.Id)
            });
    }
}
