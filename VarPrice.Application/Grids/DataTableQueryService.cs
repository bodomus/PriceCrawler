using Microsoft.EntityFrameworkCore;

namespace VarPrice.Application.Grids;

public sealed class DataTableQueryService : IDataTableQueryService
{
    public async Task<DataTableResponse<TDto>> ExecuteAsync<TEntity, TDto>(
        IQueryable<TEntity> query,
        DataTableRequest request,
        Func<TEntity, TDto> selector,
        Func<IQueryable<TEntity>, string?, IQueryable<TEntity>> filter,
        Func<IQueryable<TEntity>, int, bool, IQueryable<TEntity>> order,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(order);

        int start = Math.Max(request.Start, 0);
        int length = request.Length > 0 ? request.Length : 25;

        int recordsTotal = await query.CountAsync(ct);
        IQueryable<TEntity> filteredQuery = filter(query, request.SearchValue);
        int recordsFiltered = await filteredQuery.CountAsync(ct);
        IQueryable<TEntity> orderedQuery = order(filteredQuery, request.OrderColumn, request.OrderAscending);

        List<TEntity> entities = await orderedQuery
            .Skip(start)
            .Take(length)
            .ToListAsync(ct);

        return new DataTableResponse<TDto>
        {
            Draw = request.Draw,
            RecordsTotal = recordsTotal,
            RecordsFiltered = recordsFiltered,
            Data = entities.Select(selector).ToList()
        };
    }
}
