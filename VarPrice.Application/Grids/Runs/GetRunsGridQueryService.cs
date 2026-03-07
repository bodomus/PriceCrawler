using VarPrice.Application.Grids.Runs.Dto;

namespace VarPrice.Application.Grids.Runs;

public sealed class GetRunsGridQueryService(
    IRunsGridQuerySource querySource,
    IDataTableQueryService gridService) : IGetRunsGridQueryService
{
    public Task<DataTableResponse<RunGridRowDto>> ExecuteAsync(
        DataTableRequest request,
        CancellationToken ct = default)
    {
        var query = querySource.Build();

        return gridService.ExecuteAsync(
            query,
            request,
            row => new RunGridRowDto
            {
                Id = row.Id,
                StartedAtUtc = row.StartedAtUtc,
                FinishedAtUtc = row.FinishedAtUtc,
                Status = row.Status,
                ItemsCount = row.ItemsCount
            },
            (filteredQuery, search) =>
            {
                if (string.IsNullOrWhiteSpace(search))
                {
                    return filteredQuery;
                }

                var hasId = long.TryParse(search, out var parsedId);
                var normalizedSearch = search.ToLower();

                return filteredQuery.Where(row =>
                    (hasId && row.Id == parsedId) ||
                    row.Status.ToLower().Contains(normalizedSearch));
            },
            (orderedQuery, column, asc) => column switch
            {
                0 => asc ? orderedQuery.OrderBy(x => x.Id) : orderedQuery.OrderByDescending(x => x.Id),
                1 => asc
                    ? orderedQuery.OrderBy(x => x.StartedAtUtc)
                    : orderedQuery.OrderByDescending(x => x.StartedAtUtc),
                2 => asc ? orderedQuery.OrderBy(x => x.Status) : orderedQuery.OrderByDescending(x => x.Status),
                3 => asc
                    ? orderedQuery.OrderBy(x => x.FinishedAtUtc)
                    : orderedQuery.OrderByDescending(x => x.FinishedAtUtc),
                4 => asc ? orderedQuery.OrderBy(x => x.ItemsCount) : orderedQuery.OrderByDescending(x => x.ItemsCount),
                _ => orderedQuery.OrderByDescending(x => x.StartedAtUtc)
            },
            ct);
    }
}
