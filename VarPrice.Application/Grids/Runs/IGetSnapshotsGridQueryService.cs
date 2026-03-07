using VarPrice.Application.Grids.Runs.Dto;

namespace VarPrice.Application.Grids.Runs;

public interface IGetSnapshotsGridQueryService
{
    Task<DataTableResponse<SnapshotGridRowDto>> ExecuteAsync(
        long runId,
        DataTableRequest request,
        CancellationToken ct = default);
}
