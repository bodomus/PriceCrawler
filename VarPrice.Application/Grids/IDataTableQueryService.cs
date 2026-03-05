namespace VarPrice.Application.Grids;

public interface IDataTableQueryService
{
    Task<DataTableResponse<TDto>> ExecuteAsync<TEntity, TDto>(
        IQueryable<TEntity> query,
        DataTableRequest request,
        Func<TEntity, TDto> selector,
        Func<IQueryable<TEntity>, string?, IQueryable<TEntity>> filter,
        Func<IQueryable<TEntity>, int, bool, IQueryable<TEntity>> order);
}
