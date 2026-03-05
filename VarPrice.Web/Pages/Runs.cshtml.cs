using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VarPrice.Application.Grids;
using VarPrice.Infrastructure.Persistence;
// ReSharper disable SuggestVarOrType_SimpleTypes
// ReSharper disable All

namespace VarPrice.Web.Pages;

public sealed class RunsModel(
    VarPriceDbContext dbContext,
    IDataTableRequestParser parser,
    IDataTableQueryService gridService,
    ILogger<RunsModel> log) : PageModel
{
    public void OnGet()
    {
    }

    public Task<IActionResult> OnPostData()
    {
        var dtRequest = parser.Parse(Request);

        var query =
            from run in dbContext.CrawlerRuns.AsNoTracking()
            select new
            {
                run.Id,
                run.StartedAtUtc,
                run.FinishedAtUtc,
                run.Status,
                ItemsCount = dbContext.PriceSnapshots.Count(s => s.RunId == run.Id)
            };
        try
        {
            return ExecuteDataTableAsync(
                query,
                dtRequest,
                row => new
                {
                    id = row.Id,
                    startedAtUtc = row.StartedAtUtc,
                    finishedAtUtc = row.FinishedAtUtc,
                    status = row.Status,
                    itemsCount = row.ItemsCount
                },
                (filteredQuery, search) =>
                {
                    if (string.IsNullOrWhiteSpace(search))
                    {
                        return filteredQuery;
                    }

                    var hasId = long.TryParse(search, out var parsedId);
                    var statusPattern = $"%{search}%";

                    return filteredQuery.Where(row =>
                        (hasId && row.Id == parsedId) ||
                        EF.Functions.ILike(row.Status, statusPattern));
                },
                (orderedQuery, column, asc) => column switch
                {
                    0 => asc ? orderedQuery.OrderBy(x => x.Id) : orderedQuery.OrderByDescending(x => x.Id),
                    1 => asc ? orderedQuery.OrderBy(x => x.StartedAtUtc) : orderedQuery.OrderByDescending(x => x.StartedAtUtc),
                    2 => asc ? orderedQuery.OrderBy(x => x.Status) : orderedQuery.OrderByDescending(x => x.Status),
                    3 => asc ? orderedQuery.OrderBy(x => x.FinishedAtUtc) : orderedQuery.OrderByDescending(x => x.FinishedAtUtc),
                    4 => asc ? orderedQuery.OrderBy(x => x.ItemsCount) : orderedQuery.OrderByDescending(x => x.ItemsCount),
                    _ => orderedQuery.OrderByDescending(x => x.StartedAtUtc)
                }
            );
        }
        catch (Exception ex)
        {
            log.LogError(ex, "DataTables load failed");
            return Task.FromResult<IActionResult>(StatusCode(500, new { error = ex.Message }));
        }
    }

    public Task<IActionResult> OnPostIngestionData()
    {
        var dtRequest = parser.Parse(Request);

        var query = dbContext.IngestionRuns.AsNoTracking()
            .Select(run => new
            {
                run.Id,
                run.CrawlerRunId,
                run.StartedAtUtc,
                run.FinishedAtUtc,
                run.Status,
                run.ErrorCode
            });

        return ExecuteDataTableAsync(
            query,
            dtRequest,
            row => new
            {
                id = row.Id,
                crawlerRunId = row.CrawlerRunId,
                startedAtUtc = row.StartedAtUtc,
                finishedAtUtc = row.FinishedAtUtc,
                status = row.Status,
                errorCode = row.ErrorCode
            },
            (filteredQuery, search) =>
            {
                if (string.IsNullOrWhiteSpace(search))
                {
                    return filteredQuery;
                }

                var hasId = long.TryParse(search, out var parsedId);
                var pattern = $"%{search}%";

                return filteredQuery.Where(row =>
                    (hasId && (row.Id == parsedId || row.CrawlerRunId == parsedId)) ||
                    EF.Functions.ILike(row.Status, pattern) ||
                    (row.ErrorCode != null && EF.Functions.ILike(row.ErrorCode, pattern)));
            },
            (orderedQuery, column, asc) => column switch
            {
                0 => asc ? orderedQuery.OrderBy(x => x.Id) : orderedQuery.OrderByDescending(x => x.Id),
                1 => asc ? orderedQuery.OrderBy(x => x.CrawlerRunId) : orderedQuery.OrderByDescending(x => x.CrawlerRunId),
                2 => asc ? orderedQuery.OrderBy(x => x.StartedAtUtc) : orderedQuery.OrderByDescending(x => x.StartedAtUtc),
                3 => asc ? orderedQuery.OrderBy(x => x.FinishedAtUtc) : orderedQuery.OrderByDescending(x => x.FinishedAtUtc),
                4 => asc ? orderedQuery.OrderBy(x => x.Status) : orderedQuery.OrderByDescending(x => x.Status),
                5 => asc ? orderedQuery.OrderBy(x => x.ErrorCode) : orderedQuery.OrderByDescending(x => x.ErrorCode),
                _ => orderedQuery.OrderByDescending(x => x.StartedAtUtc)
            });
    }

    public Task<IActionResult> OnPostSnapshotsData()
    {
        var dtRequest = parser.Parse(Request);

        var query = dbContext.PriceSnapshots.AsNoTracking()
            .Select(snapshot => new
            {
                snapshot.Id,
                snapshot.RunId,
                snapshot.CapturedAtUtc,
                snapshot.City,
                snapshot.Price,
                snapshot.OldPrice,
                snapshot.PromoFlag,
                snapshot.InStock
            });

        return ExecuteDataTableAsync(
            query,
            dtRequest,
            row => new
            {
                id = row.Id,
                runId = row.RunId,
                capturedAtUtc = row.CapturedAtUtc,
                city = row.City,
                price = row.Price,
                oldPrice = row.OldPrice,
                promoFlag = row.PromoFlag,
                inStock = row.InStock
            },
            (filteredQuery, search) =>
            {
                if (string.IsNullOrWhiteSpace(search))
                {
                    return filteredQuery;
                }

                var hasId = long.TryParse(search, out var parsedId);
                var pattern = $"%{search}%";

                return filteredQuery.Where(row =>
                    (hasId && (row.Id == parsedId || row.RunId == parsedId)) ||
                    (row.City != null && EF.Functions.ILike(row.City, pattern)));
            },
            (orderedQuery, column, asc) => column switch
            {
                0 => asc ? orderedQuery.OrderBy(x => x.Id) : orderedQuery.OrderByDescending(x => x.Id),
                1 => asc ? orderedQuery.OrderBy(x => x.RunId) : orderedQuery.OrderByDescending(x => x.RunId),
                2 => asc ? orderedQuery.OrderBy(x => x.CapturedAtUtc) : orderedQuery.OrderByDescending(x => x.CapturedAtUtc),
                3 => asc ? orderedQuery.OrderBy(x => x.City) : orderedQuery.OrderByDescending(x => x.City),
                4 => asc ? orderedQuery.OrderBy(x => x.Price) : orderedQuery.OrderByDescending(x => x.Price),
                5 => asc ? orderedQuery.OrderBy(x => x.OldPrice) : orderedQuery.OrderByDescending(x => x.OldPrice),
                6 => asc ? orderedQuery.OrderBy(x => x.PromoFlag) : orderedQuery.OrderByDescending(x => x.PromoFlag),
                7 => asc ? orderedQuery.OrderBy(x => x.InStock) : orderedQuery.OrderByDescending(x => x.InStock),
                _ => orderedQuery.OrderByDescending(x => x.CapturedAtUtc)
            });
    }

    private async Task<IActionResult> ExecuteDataTableAsync<TRow, TDto>(
        IQueryable<TRow> query,
        DataTableRequest dtRequest,
        Func<TRow, TDto> selector,
        Func<IQueryable<TRow>, string?, IQueryable<TRow>> filter,
        Func<IQueryable<TRow>, int, bool, IQueryable<TRow>> order)
    {
        try
        {
            var response = await gridService.ExecuteAsync(query, dtRequest, selector, filter, order);
            return new JsonResult(response);
        }
        catch (Exception ex)
        {

            log.LogError(ex, "DataTables load failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
