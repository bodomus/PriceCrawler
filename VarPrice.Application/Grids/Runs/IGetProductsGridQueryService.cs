using VarPrice.Application.Grids.Runs.Dto;

namespace VarPrice.Application.Grids.Runs;

public interface IGetProductsGridQueryService
{
    Task<DataTableResponse<ProductGridRowDto>> ExecuteAsync(
        long snapshotId,
        DataTableRequest request,
        CancellationToken ct = default);
}
