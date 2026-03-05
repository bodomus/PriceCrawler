using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VarPrice.Application.Grids;
using VarPrice.Infrastructure.Persistence;

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

    public Task<IActionResult> OnPostRunsData()
    {
        var dtRequest = parser.Parse(Request);

        var query = dbContext.CrawlerRuns.AsNoTracking()
            .Select(run => new
            {
                run.Id,
                run.StartedAtUtc,
                run.FinishedAtUtc,
                run.Status,
                ItemsCount = dbContext.PriceSnapshots.Count(s => s.RunId == run.Id)
            });

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
            });
    }

    public Task<IActionResult> OnPostSnapshotsData(long? runId)
    {
        var dtRequest = parser.Parse(Request);
        if (runId is null)
        {
            return EmptyDataTableResult(dtRequest.Draw);
        }

        var query = dbContext.PriceSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.RunId == runId.Value)
            .Select(snapshot => new
            {
                snapshot.Id,
                snapshot.CapturedAtUtc,
                snapshot.City,
                snapshot.Price,
                snapshot.OldPrice,
                DiscountPercent = snapshot.OldPrice.HasValue && snapshot.OldPrice.Value > 0
                    ? ((snapshot.OldPrice.Value - snapshot.Price) / snapshot.OldPrice.Value) * 100m
                    : (decimal?)null,
                snapshot.PromoFlag,
                snapshot.InStock
            });

        return ExecuteDataTableAsync(
            query,
            dtRequest,
            row => new
            {
                id = row.Id,
                createdAtUtc = row.CapturedAtUtc,
                city = row.City,
                price = row.Price,
                oldPrice = row.OldPrice,
                discountPercent = row.DiscountPercent,
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
                return hasId
                    ? filteredQuery.Where(row => row.Id == parsedId)
                    : filteredQuery.Where(_ => false);
            },
            (orderedQuery, column, asc) => column switch
            {
                0 => asc ? orderedQuery.OrderBy(x => x.Id) : orderedQuery.OrderByDescending(x => x.Id),
                1 => asc ? orderedQuery.OrderBy(x => x.CapturedAtUtc) : orderedQuery.OrderByDescending(x => x.CapturedAtUtc),
                2 => asc ? orderedQuery.OrderBy(x => x.City) : orderedQuery.OrderByDescending(x => x.City),
                3 => asc ? orderedQuery.OrderBy(x => x.Price) : orderedQuery.OrderByDescending(x => x.Price),
                4 => asc ? orderedQuery.OrderBy(x => x.OldPrice) : orderedQuery.OrderByDescending(x => x.OldPrice),
                5 => asc ? orderedQuery.OrderBy(x => x.DiscountPercent) : orderedQuery.OrderByDescending(x => x.DiscountPercent),
                6 => asc ? orderedQuery.OrderBy(x => x.PromoFlag) : orderedQuery.OrderByDescending(x => x.PromoFlag),
                7 => asc ? orderedQuery.OrderBy(x => x.InStock) : orderedQuery.OrderByDescending(x => x.InStock),
                _ => orderedQuery.OrderByDescending(x => x.CapturedAtUtc)
            });
    }

    public Task<IActionResult> OnPostProductsData(long? snapshotId)
    {
        var dtRequest = parser.Parse(Request);
        if (snapshotId is null)
        {
            return EmptyDataTableResult(dtRequest.Draw);
        }

        var query = dbContext.Database.SqlQuery<ProductGridRow>($"""
            select
                p.product_key as "ProductKey",
                p.name as "Name",
                p.product_id as "ProductId",
                p.url as "Url",
                p.pack_value as "PackValue",
                p.pack_unit as "PackUnit",
                p.created_at as "CreatedAtUtc",
                s.price as "SnapshotPrice"
            from price_snapshot s
            join product p on p.product_key = s.product_key
            where s.snapshot_id = {snapshotId.Value}
            """);

        return ExecuteDataTableAsync(
            query,
            dtRequest,
            row => new
            {
                id = row.ProductKey,
                name = row.Name,
                sku = row.ProductId,
                url = row.Url,
                price = row.SnapshotPrice,
                unit = FormatUnit(row.PackValue, row.PackUnit),
                updatedAtUtc = row.CreatedAtUtc
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
                    (hasId && row.ProductKey == parsedId) ||
                    EF.Functions.ILike(row.Name, pattern) ||
                    EF.Functions.ILike(row.ProductId, pattern));
            },
            (orderedQuery, column, asc) => column switch
            {
                0 => asc ? orderedQuery.OrderBy(x => x.ProductKey) : orderedQuery.OrderByDescending(x => x.ProductKey),
                1 => asc ? orderedQuery.OrderBy(x => x.Name) : orderedQuery.OrderByDescending(x => x.Name),
                2 => asc ? orderedQuery.OrderBy(x => x.ProductId) : orderedQuery.OrderByDescending(x => x.ProductId),
                3 => asc ? orderedQuery.OrderBy(x => x.Url) : orderedQuery.OrderByDescending(x => x.Url),
                4 => asc ? orderedQuery.OrderBy(x => x.SnapshotPrice) : orderedQuery.OrderByDescending(x => x.SnapshotPrice),
                5 => asc ? orderedQuery.OrderBy(x => x.PackUnit) : orderedQuery.OrderByDescending(x => x.PackUnit),
                6 => asc ? orderedQuery.OrderBy(x => x.CreatedAtUtc) : orderedQuery.OrderByDescending(x => x.CreatedAtUtc),
                _ => orderedQuery.OrderBy(x => x.Name)
            });
    }

    private Task<IActionResult> EmptyDataTableResult(int draw)
    {
        var response = new DataTableResponse<object>
        {
            Draw = draw,
            RecordsTotal = 0,
            RecordsFiltered = 0,
            Data = []
        };

        return Task.FromResult<IActionResult>(new JsonResult(response));
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

    private static string? FormatUnit(decimal? packValue, string? packUnit)
    {
        if (packValue is null && string.IsNullOrWhiteSpace(packUnit))
        {
            return null;
        }

        if (packValue is null)
        {
            return packUnit;
        }

        return string.IsNullOrWhiteSpace(packUnit)
            ? packValue.Value.ToString("0.###")
            : $"{packValue.Value:0.###} {packUnit}";
    }

    private sealed class ProductGridRow
    {
        public long ProductKey { get; init; }

        public string ProductId { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string Url { get; init; } = string.Empty;

        public decimal? PackValue { get; init; }

        public string? PackUnit { get; init; }

        public DateTime CreatedAtUtc { get; init; }

        public decimal SnapshotPrice { get; init; }
    }
}
