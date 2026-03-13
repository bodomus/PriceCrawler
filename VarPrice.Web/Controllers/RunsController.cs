using System.Reflection;

using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Grids.Runs;
using VarPrice.Application.Grids.Runs.Dto;
using VarPrice.Application.Models;
using VarPrice.Web.ViewModels.Runs;
using VarPrice.Web.ViewModels.Shared;

namespace VarPrice.Web.Controllers;

public sealed class RunsController(
    IRunsGridQuerySource runsGridQuerySource,
    ISnapshotsGridQuerySource snapshotsGridQuerySource,
    IProductsGridQuerySource productsGridQuerySource,
    IRunCrawlerUseCase runCrawlerUseCase,
    ILogger<RunsController> log) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View(CreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IngestVegetables(CancellationToken ct)
    {
        var result = await runCrawlerUseCase.RunVegetablesAsync(ct);
        var isSuccess = string.Equals(result.Status, "ok", StringComparison.OrdinalIgnoreCase);

        return View("Index", CreateViewModel(
            latestRun: result,
            statusBar: isSuccess
                ? new StatusBarViewModel("info", "Price snapshot collected successfully.")
                : CreateRunFailureStatus(result)));
    }

    [HttpGet]
    public async Task<IActionResult> RunsGrid(DataSourceLoadOptions loadOptions, CancellationToken ct)
    {
        try
        {
            var query = runsGridQuerySource.Build()
                .Select(row => new RunGridRowDto
                {
                    Id = row.Id,
                    StartedAtUtc = row.StartedAtUtc,
                    FinishedAtUtc = row.FinishedAtUtc,
                    Status = row.Status,
                    ItemsCount = row.ItemsCount
                });

            var result = await DataSourceLoader.LoadAsync(query, loadOptions, ct);
            return Json(result);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "DevExtreme load failed for {Operation}", nameof(RunsGrid));
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> SnapshotsGrid(long? runId, DataSourceLoadOptions loadOptions, CancellationToken ct)
    {
        if (runId is null)
        {
            return Json(DataSourceLoader.Load(Array.Empty<SnapshotGridRowDto>(), loadOptions));
        }

        try
        {
            var query = snapshotsGridQuerySource.Build(runId.Value)
                .Select(row => new SnapshotGridRowDto
                {
                    //TODO check invalid city parser
                    Id = row.Id,
                    CreatedAtUtc = row.CapturedAtUtc,
                    City = row.City,
                    Price = row.FinalPrice,
                    OldPrice = row.RegularPrice,
                    DiscountPercent = row.DiscountPercent,
                    PromoFlag = row.PromoFlag,
                    InStock = row.InStock
                });

            var result = await DataSourceLoader.LoadAsync(query, loadOptions, ct);
            return Json(result);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "DevExtreme load failed for {Operation}", nameof(SnapshotsGrid));
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ProductsGrid(long? snapshotId, DataSourceLoadOptions loadOptions,
        CancellationToken ct)
    {
        if (snapshotId is null)
        {
            return Json(DataSourceLoader.Load(Array.Empty<ProductGridRowDto>(), loadOptions));
        }

        try
        {
            var records = await productsGridQuerySource.Build(snapshotId.Value)
                .Select(row => new
                {
                    row.ProductKey,
                    row.Name,
                    row.ProductId,
                    row.Url,
                    row.SnapshotPrice,
                    row.PackValue,
                    row.PackUnit,
                    row.LastSeenAtUtc
                })
                .ToListAsync(ct);

            var result = DataSourceLoader.Load(
                records.Select(row => new ProductGridRowDto
                {
                    Id = row.ProductKey,
                    Name = row.Name,
                    Sku = row.ProductId,
                    Url = row.Url,
                    Price = row.SnapshotPrice,
                    Unit = FormatUnit(row.PackValue, row.PackUnit),
                    UpdatedAtUtc = row.LastSeenAtUtc
                }),
                loadOptions);

            return Json(result);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "DevExtreme load failed for {Operation}", nameof(ProductsGrid));
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    private static string? FormatUnit(decimal? packValue, string? packUnit)
    {
        if (packValue is null && string.IsNullOrWhiteSpace(packUnit))
        {
            return null;
        }

        if (packValue is null)
        {
            return packUnit;
        }

        return string.IsNullOrWhiteSpace(packUnit)
            ? packValue.Value.ToString("0.###")
            : $"{packValue.Value:0.###} {packUnit}";
    }

    private RunsDashboardVm CreateViewModel(CrawlerRunResult? latestRun = null, StatusBarViewModel? statusBar = null)
    {
        return new RunsDashboardVm
        {
            AppVersion = ResolveAppVersion(),
            LatestRun = latestRun,
            PageTitle = "VARUS - Dashboard",
            StatusBar = statusBar
        };
    }

    private static StatusBarViewModel CreateRunFailureStatus(CrawlerRunResult result)
    {
        var message = string.IsNullOrWhiteSpace(result.Note)
            ? $"Crawler run completed with status '{result.Status}'."
            : $"Crawler run completed with status '{result.Status}'. {result.Note}";
        return new StatusBarViewModel("error", message);
    }

    private static string ResolveAppVersion()
    {
        var assembly = typeof(RunsController).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
