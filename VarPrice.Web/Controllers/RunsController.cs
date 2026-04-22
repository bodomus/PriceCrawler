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
    IProductAnalysisService productAnalysisService,
    IProductPriceHistoryQuerySource productPriceHistoryQuerySource,
    IProductCardExtractor productCardExtractor,
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
                            var nodes = new List<RunTreeNodeVm>
                            {
                                new()
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
                                new()
                                {
                                    Id = $"run:{row.Id}:successful",
                                    ParentId = runNodeId,
                                    NodeType = "successful",
                                    Title = $"Successful snapshots ({row.SuccessfulSnapshotsCount})",
                                    RunId = row.Id,
                                    SnapshotScope = SnapshotScopes.Successful,
                                    ItemsCount = row.SuccessfulSnapshotsCount
                                }
                            };

                            if (row.FailedSnapshotsCount > 0)
                            {
                                nodes.Add(new RunTreeNodeVm
                                {
                                    Id = $"run:{row.Id}:failed",
                                    ParentId = runNodeId,
                                    NodeType = "failed",
                                    Title = $"Failed snapshots ({row.FailedSnapshotsCount})",
                                    RunId = row.Id,
                                    SnapshotScope = SnapshotScopes.Failed,
                                    ItemsCount = row.FailedSnapshotsCount
                                });
                            }

                            return nodes;
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

    [HttpGet]
    public async Task<IActionResult> ProductDetails(long? snapshotId, CancellationToken ct)
    {
        if (snapshotId is null)
        {
            return Json(null);
        }

        try
        {
            var analysis = await productAnalysisService.GetAsync(snapshotId.Value, ct);
            return Json(analysis?.ProductCard);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Product details load failed for snapshot {SnapshotId}", snapshotId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ProductHistory(long? snapshotId, [DataSourceRequest] DataSourceRequest request,
        CancellationToken ct)
    {
        if (snapshotId is null)
        {
            return Json(Array.Empty<ProductPriceHistoryRowDto>().ToDataSourceResult(request));
        }

        try
        {
            var resultQuery = productPriceHistoryQuerySource.Build(snapshotId.Value)
                .OrderByDescending(row => row.CapturedAtUtc)
                .Select(row => new ProductPriceHistoryRowDto
                {
                    Id = row.Id,
                    RunId = row.RunId,
                    CapturedAtUtc = row.CapturedAtUtc,
                    Price = row.Price,
                    OldPrice = row.OldPrice,
                    DiscountPercent = row.DiscountPercent,
                    PromoFlag = row.PromoFlag,
                    InStock = row.InStock,
                    Source = row.Source
                });

            var result = await resultQuery.ToDataSourceResultAsync(request, ct);
            return Json(result);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Price history load failed for snapshot {SnapshotId}", snapshotId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ProductAnalytics(long? snapshotId, CancellationToken ct)
    {
        if (snapshotId is null)
        {
            return Json(null);
        }

        try
        {
            var analysis = await productAnalysisService.GetAsync(snapshotId.Value, ct);
            return Json(analysis?.Analytics);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Product analytics load failed for snapshot {SnapshotId}", snapshotId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ProductAnalysis(long? snapshotId, CancellationToken ct)
    {
        if (snapshotId is null)
        {
            return Json(null);
        }

        try
        {
            var analysis = await productAnalysisService.GetAsync(snapshotId.Value, ct);
            return Json(analysis);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Unified product analysis load failed for snapshot {SnapshotId}", snapshotId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefreshLiveProduct(long? snapshotId, CancellationToken ct)
    {
        if (snapshotId is null)
        {
            return BadRequest(new { error = "snapshotId is required." });
        }

        try
        {
            var analysis = await productAnalysisService.GetAsync(snapshotId.Value, ct);
            var snapshot = analysis?.ProductCard;
            if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Url))
            {
                return NotFound(new
                    { error = $"Snapshot #{snapshotId.Value} does not have a resolvable product URL." });
            }

            var liveResult = await productCardExtractor.ExtractAsync(snapshot.Url, ct);
            return Json(MapLiveResult(snapshotId.Value, snapshot.Url, liveResult));
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Manual live product refresh failed for snapshot {SnapshotId}", snapshotId);
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

    private static Task<T?> FirstOrDefaultAsyncOrSync<T>(IQueryable<T> query, CancellationToken ct)
    {
        if (query.Provider is IAsyncQueryProvider)
        {
            return EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(query, ct);
        }

        return Task.FromResult(query.FirstOrDefault());
    }

    private static ProductLiveResultDto MapLiveResult(long snapshotId, string requestedUrl,
        ProductExtractResult liveResult)
    {
        var status = liveResult.IsSuccess
            ? "success"
            : liveResult.HasCard
                ? "partial"
                : "error";

        return new ProductLiveResultDto
        {
            SnapshotId = snapshotId,
            RequestedAtUtc = DateTime.UtcNow,
            RequestedUrl = requestedUrl,
            Status = status,
            HttpStatus = liveResult.HttpStatus,
            LatencyMs = liveResult.LatencyMs,
            ApproximateRps = Math.Round(liveResult.ApproximateRps, 2, MidpointRounding.AwayFromZero),
            LiveCard = liveResult.Card is null
                ? null
                : new ProductLiveCardDto
                {
                    Sku = liveResult.Card.ExternalId,
                    Name = liveResult.Card.Name,
                    Url = liveResult.Card.Url,
                    Slug = liveResult.Card.Slug,
                    Unit = FormatUnit(liveResult.Card.PackValue, liveResult.Card.PackUnit),
                    CurrentPrice = liveResult.Card.Price,
                    OldPrice = liveResult.Card.OldPrice,
                    PromoFlag = liveResult.Card.PromoFlag,
                    InStock = liveResult.Card.InStock
                },
            Issue = liveResult.Issue is null
                ? null
                : new ProductLiveIssueDto
                {
                    Stage = liveResult.Issue.Stage,
                    ErrorCode = liveResult.Issue.ErrorCode,
                    HttpStatus = liveResult.Issue.HttpStatus,
                    Message = liveResult.Issue.Message,
                    IsTransient = liveResult.Issue.IsTransient,
                    IsCritical = liveResult.Issue.IsCritical
                }
        };
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
