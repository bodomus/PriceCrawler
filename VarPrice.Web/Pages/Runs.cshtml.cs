using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using VarPrice.Infrastructure.Persistence;

namespace VarPrice.Web.Pages;

public sealed class RunsModel(VarPriceDbContext dbContext, ILogger<RunsModel> log) : PageModel
{
    private const int DefaultPageLength = 25;
    private const int MaxPageLength = 200;
    //private ILogger<RunsModel> _logger;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostData(ILogger<RunsModel> log)
    {
        var form = Request.Form;
        var ct = HttpContext.RequestAborted;

        var draw = ParseInt(form["draw"], 0);
        var start = Math.Max(ParseInt(form["start"], 0), 0);

        var requestedLength = ParseInt(form["length"], DefaultPageLength);
        var length = requestedLength switch
        {
            < 1 => DefaultPageLength,
            > MaxPageLength => MaxPageLength,
            _ => requestedLength
        };

        var search = form["search[value]"].ToString().Trim();
        var orderColumn = ParseInt(form["order[0][column]"], 1);
        var orderDir = form["order[0][dir]"].ToString();
        var descending = string.Equals(orderDir, "desc", StringComparison.OrdinalIgnoreCase);

        var snapshotCounts = dbContext.PriceSnapshots.AsNoTracking()
            .GroupBy(snapshot => snapshot.RunId)
            .Select(group => new
            {
                RunId = group.Key,
                ItemsCount = group.Count()
            });

        var query = from run in dbContext.CrawlerRuns.AsNoTracking()
                    join countRow in snapshotCounts
                        on run.Id equals countRow.RunId into countGroup
                    from countRow in countGroup.DefaultIfEmpty()
                    select new CrawlerRunRow(
                        run.Id,
                        run.StartedAtUtc,
                        run.FinishedAtUtc,
                        run.Status,
                        countRow == null ? 0 : countRow.ItemsCount);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var hasId = long.TryParse(search, out var parsedId);
            var statusPattern = $"%{search}%";

            query = query.Where(row =>
                (hasId && row.Id == parsedId) ||
                EF.Functions.ILike(row.Status, statusPattern));
        }

        query = ApplyOrdering(query, orderColumn, descending);

        var recordsTotal = await dbContext.CrawlerRuns.AsNoTracking().CountAsync(ct);
        var recordsFiltered = await query.CountAsync(ct);

        var page = await query
            .Skip(start)
            .Take(length)
            .Select(row => new
            {
                id = row.Id,
                startedAtUtc = row.StartedAtUtc,
                finishedAtUtc = row.FinishedAtUtc,
                status = row.Status,
                itemsCount = row.ItemsCount
            })
            .ToListAsync(ct);

        try
        {
            return new JsonResult(new
            {
                draw,
                recordsTotal,
                recordsFiltered,
                data = page.ToList()
            });
        }catch (Exception ex)
        {
            log.LogError(ex, "DataTables load failed");
            return StatusCode(500, new { error = ex.Message }); // временно для диагностики
        }
    }

    private static IQueryable<CrawlerRunRow> ApplyOrdering(IQueryable<CrawlerRunRow> query, int orderColumn, bool descending)
    {
        return orderColumn switch
        {
            0 => descending ? query.OrderByDescending(x => x.Id) : query.OrderBy(x => x.Id),
            1 => descending ? query.OrderByDescending(x => x.StartedAtUtc) : query.OrderBy(x => x.StartedAtUtc),
            2 => descending ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
            3 => descending ? query.OrderByDescending(x => x.FinishedAtUtc) : query.OrderBy(x => x.FinishedAtUtc),
            4 => descending ? query.OrderByDescending(x => x.ItemsCount) : query.OrderBy(x => x.ItemsCount),
            _ => query.OrderByDescending(x => x.StartedAtUtc)
        };
    }

    private static int ParseInt(string? value, int defaultValue)
    {
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }
}
