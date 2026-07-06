using PriceCrawler.Application.Grids.Runs.Dto;

namespace PriceCrawler.Application.Grids.Runs;

public interface IProductAnalysisService
{
    Task<ProductAnalysisDto?> GetAsync(long snapshotId, CancellationToken ct);
}
