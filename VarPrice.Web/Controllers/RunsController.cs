using System.Reflection;

using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Grids.Runs;
using VarPrice.Application.Grids.Runs.Dto;
using VarPrice.Application.Models;
using VarPrice.Web.ViewModels.Runs;
using VarPrice.Web.ViewModels.Shared;

namespace VarPrice.Web.Controllers;

public sealed class RunsController(
    IRunsGridQuerySource runsGridQuerySource,
    IRunsTreeQuerySource runsTreeQuerySource,
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
    public async Task<IActionResult> RunsGrid([DataSourceRequest] DataSourceRequest request, CancellationToken ct)
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

            var result = await query.ToDataSourceResultAsync(request, ct);
            return Json(result);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Kendo load failed for {Operation}", nameof(RunsGrid));
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> RunsTree(CancellationToken ct)
    {
        try
        {
            var runs = await ToListAsyncOrSync(
                runsTreeQuerySource.Build()
                    .OrderByDescending(row => row.StartedAtUtc),
                ct);

            var nodes = runs
                .GroupBy(row => row.StartedAtUtc.Date)
                .OrderByDescending(group => group.Key)
                .SelectMany(group =>
                {
                    var dateNodeId = $"date:{group.Key:yyyy-MM-dd}";
                    var dateNode = new RunTreeNodeVm
                    {
                        Id = dateNodeId,
                        NodeType = "date",
                        Title = $"{group.Key:dd.MM.yyyy} ({group.Count()})",
                        SnapshotScope = SnapshotScopes.None,
                        ItemsCount = group.Count()
                    };

                    var runNodes = group
                        .OrderByDescending(row => row.StartedAtUtc)
                        .SelectMany(row =>
                        {
                            var runNodeId = $"run:{row.Id}";
                            return new[]
                            {
                                new RunTreeNodeVm
                                {
                                    Id = runNodeId,
                                    ParentId = dateNodeId,
                                    NodeType = "run",
                                    Title = $"Run #{row.Id}",
                                    RunId = row.Id,
                                    SnapshotScope = SnapshotScopes.All,
                                    StartedAtUtc = row.StartedAtUtc,
                                    FinishedAtUtc = row.FinishedAtUtc,
                                    Status = row.Status,
                                    ItemsCount = row.ItemsCount
                                },
                                new RunTreeNodeVm
                                {
                                    Id = $"run:{row.Id}:successful",
                                    ParentId = runNodeId,
                                    NodeType = "successful",
                                    Title = $"Successful snapshots ({row.SuccessfulSnapshotsCount})",
                                    RunId = row.Id,
                                    SnapshotScope = SnapshotScopes.Successful,
                                    ItemsCount = row.SuccessfulSnapshotsCount
                                },
                                new RunTreeNodeVm
                                {
                                    Id = $"run:{row.Id}:failed",
                                    ParentId = runNodeId,
                                    NodeType = "failed",
                                    Title = $"Failed snapshots ({row.FailedSnapshotsCount})",
                                    RunId = row.Id,
                                    SnapshotScope = SnapshotScopes.Failed,
                                    ItemsCount = row.FailedSnapshotsCount
                                }
                            };
                        });

                    return new[] { dateNode }.Concat(runNodes);
                })
                .ToList();

            return Json(nodes);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Kendo load failed for {Operation}", nameof(RunsTree));
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> SnapshotsGrid(long? runId, string? snapshotScope,
        [DataSourceRequest] DataSourceRequest request,
        CancellationToken ct)
    {
        if (runId is null)
        {
            return Json(Array.Empty<SnapshotGridRowDto>().ToDataSourceResult(request));
        }

        try
        {
            var query = snapshotsGridQuerySource.Build(runId.Value);
            var normalizedScope = NormalizeSnapshotScope(snapshotScope);

            query = normalizedScope switch
            {
                SnapshotScopes.Successful => query.Where(row => row.IsSuccessful),
                SnapshotScopes.Failed => query.Where(row => !row.IsSuccessful),
                _ => query
            };

            var resultQuery = query
                .Select(row => new SnapshotGridRowDto
                {
                    Id = row.Id,
                    CreatedAtUtc = row.CapturedAtUtc,
                    Price = row.Price,
                    OldPrice = row.OldPrice,
                    DiscountPercent = row.DiscountPercent,
                    PromoFlag = row.PromoFlag,
                    InStock = row.InStock,
                    Status = row.IsSuccessful ? "OK" : "Failed"
                });

            var result = await resultQuery.ToDataSourceResultAsync(request, ct);
            return Json(result);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Kendo load failed for {Operation}", nameof(SnapshotsGrid));
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ProductsGrid(long? snapshotId, [DataSourceRequest] DataSourceRequest request,
        CancellationToken ct)
    {
        if (snapshotId is null)
        {
            return Json(Array.Empty<ProductGridRowDto>().ToDataSourceResult(request));
        }

        try
        {
            var records = await productsGridQuerySource.Build(snapshotId.Value)
                .Select(row => new
                {
                    row.Id,
                    row.Name,
                    row.ExternalId,
                    row.Url,
                    row.SnapshotPrice,
                    row.PackValue,
                    row.PackUnit,
                    row.UpdatedAtUtc
                })
                .ToListAsync(ct);

            var result = records
                .Select(row => new ProductGridRowDto
                {
                    Id = row.Id,
                    Name = row.Name,
                    Sku = row.ExternalId ?? string.Empty,
                    Url = row.Url,
                    Price = row.SnapshotPrice,
                    Unit = FormatUnit(row.PackValue, row.PackUnit),
                    UpdatedAtUtc = row.UpdatedAtUtc
                })
                .ToDataSourceResult(request);

            return Json(result);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Kendo load failed for {Operation}", nameof(ProductsGrid));
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

    private static string NormalizeSnapshotScope(string? snapshotScope)
    {
        return snapshotScope?.Trim().ToLowerInvariant() switch
        {
            SnapshotScopes.Successful => SnapshotScopes.Successful,
            SnapshotScopes.Failed => SnapshotScopes.Failed,
            _ => SnapshotScopes.All
        };
    }

    private static Task<List<T>> ToListAsyncOrSync<T>(IQueryable<T> query, CancellationToken ct)
    {
        if (query.Provider is IAsyncQueryProvider)
        {
            return EntityFrameworkQueryableExtensions.ToListAsync(query, ct);
        }

        return Task.FromResult(query.ToList());
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
