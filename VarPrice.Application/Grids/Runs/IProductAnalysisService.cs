using VarPrice.Application.Grids.Runs.Dto;

namespace VarPrice.Application.Grids.Runs;

public interface IProductAnalysisService
{
    Task<ProductAnalysisDto?> GetAsync(long snapshotId, CancellationToken ct);
}
