using Microsoft.AspNetCore.Mvc;

using VarPrice.Application.Grids;
using VarPrice.Application.Grids.Runs;
using VarPrice.Web.Infrastructure.DataTables;
using VarPrice.Web.ViewModels.Runs;

namespace VarPrice.Web.Controllers;

public sealed class RunsController(
    IDataTableRequestParser parser,
    IGetRunsGridQueryService getRunsGridQueryService,
    IGetSnapshotsGridQueryService getSnapshotsGridQueryService,
    IGetProductsGridQueryService getProductsGridQueryService,
    ILogger<RunsController> log) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View(new RunsDashboardVm());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RunsData(CancellationToken ct)
    {
        var dtRequest = parser.Parse(Request);
        return ExecuteSafelyAsync(
            nameof(RunsData),
            () => getRunsGridQueryService.ExecuteAsync(dtRequest, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> SnapshotsData(long? runId, CancellationToken ct)
    {
        var dtRequest = parser.Parse(Request);
        if (runId is null)
        {
            return Task.FromResult<IActionResult>(DataTableResults.Empty(dtRequest.Draw));
        }

        return ExecuteSafelyAsync(
            nameof(SnapshotsData),
            () => getSnapshotsGridQueryService.ExecuteAsync(runId.Value, dtRequest, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ProductsData(long? snapshotId, CancellationToken ct)
    {
        var dtRequest = parser.Parse(Request);
        if (snapshotId is null)
        {
            return Task.FromResult<IActionResult>(DataTableResults.Empty(dtRequest.Draw));
        }

        return ExecuteSafelyAsync(
            nameof(ProductsData),
            () => getProductsGridQueryService.ExecuteAsync(snapshotId.Value, dtRequest, ct));
    }

    private async Task<IActionResult> ExecuteSafelyAsync<TDto>(
        string operation,
        Func<Task<DataTableResponse<TDto>>> execute)
    {
        try
        {
            var response = await execute();
            return Json(response);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "DataTables load failed for {Operation}", operation);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }
}
