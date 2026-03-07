using VarPrice.Application.Grids.Runs.Dto;

namespace VarPrice.Application.Grids.Runs;

public interface IGetRunsGridQueryService
{
    Task<DataTableResponse<RunGridRowDto>> ExecuteAsync(
        DataTableRequest request,
        CancellationToken ct = default);
}
