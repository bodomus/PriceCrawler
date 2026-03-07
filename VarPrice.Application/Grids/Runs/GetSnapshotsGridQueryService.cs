using VarPrice.Application.Grids.Runs.Dto;

namespace VarPrice.Application.Grids.Runs;

public sealed class GetSnapshotsGridQueryService(
    ISnapshotsGridQuerySource querySource,
    IDataTableQueryService gridService) : IGetSnapshotsGridQueryService
{
    public Task<DataTableResponse<SnapshotGridRowDto>> ExecuteAsync(
        long runId,
        DataTableRequest request,
        CancellationToken ct = default)
    {
        var query = querySource.Build(runId);

        return gridService.ExecuteAsync(
            query,
            request,
            row => new SnapshotGridRowDto
            {
                Id = row.Id,
                CreatedAtUtc = row.CapturedAtUtc,
                City = row.City,
                Price = row.Price,
                OldPrice = row.OldPrice,
                DiscountPercent = row.DiscountPercent,
                PromoFlag = row.PromoFlag,
                InStock = row.InStock
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
                1 => asc
                    ? orderedQuery.OrderBy(x => x.CapturedAtUtc)
                    : orderedQuery.OrderByDescending(x => x.CapturedAtUtc),
                2 => asc ? orderedQuery.OrderBy(x => x.City) : orderedQuery.OrderByDescending(x => x.City),
                3 => asc ? orderedQuery.OrderBy(x => x.Price) : orderedQuery.OrderByDescending(x => x.Price),
                4 => asc ? orderedQuery.OrderBy(x => x.OldPrice) : orderedQuery.OrderByDescending(x => x.OldPrice),
                5 => asc
                    ? orderedQuery.OrderBy(x => x.DiscountPercent)
                    : orderedQuery.OrderByDescending(x => x.DiscountPercent),
                6 => asc ? orderedQuery.OrderBy(x => x.PromoFlag) : orderedQuery.OrderByDescending(x => x.PromoFlag),
                7 => asc ? orderedQuery.OrderBy(x => x.InStock) : orderedQuery.OrderByDescending(x => x.InStock),
                _ => orderedQuery.OrderByDescending(x => x.CapturedAtUtc)
            },
            ct);
    }
}
