using System.Collections;

using Kendo.Mvc.UI;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Grids.Runs;
using VarPrice.Application.Grids.Runs.Dto;
using VarPrice.Application.Grids.Runs.QueryRows;
using VarPrice.Application.Models;
using VarPrice.Web.Controllers;
using VarPrice.Web.ViewModels.Runs;

namespace VarPrice.Web.Tests;

public sealed class RunsControllerTests
{
    [Fact]
    public void Index_ReturnsDashboardViewModel()
    {
        var sut = CreateController(
            new CrawlerRunResult(7, "ok", 12, 0, "processed=12, errors=0"));

        var result = sut.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<RunsDashboardVm>(view.Model);
        Assert.Equal("VARUS - Dashboard", model.PageTitle);
        Assert.False(string.IsNullOrWhiteSpace(model.AppVersion));
    }

    [Fact]
    public async Task IngestVegetables_WhenRunFails_ShowsErrorStatusMessage()
    {
        var failedRun = new CrawlerRunResult(18, "error", 0, 1, "Database is unavailable.");
        var sut = CreateController(failedRun);

        var result = await sut.IngestVegetables(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<RunsDashboardVm>(view.Model);
        Assert.Equal(failedRun, model.LatestRun);
        Assert.NotNull(model.StatusBar);
        Assert.Equal("error", model.StatusBar!.Level);
        Assert.Contains("Crawler run completed with status 'error'.", model.StatusBar.Message);
        Assert.Contains("Database is unavailable.", model.StatusBar.Message);
    }

    [Fact]
    public async Task IngestVegetables_WhenRunSucceeds_ShowsLatestRunSummary()
    {
        var run = new CrawlerRunResult(17, "ok", 24, 1, "processed=24, errors=1");
        var sut = CreateController(run);

        var result = await sut.IngestVegetables(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<RunsDashboardVm>(view.Model);
        Assert.Equal(run, model.LatestRun);
        Assert.NotNull(model.StatusBar);
        Assert.Equal("info", model.StatusBar!.Level);
        Assert.Equal("Price snapshot collected successfully.", model.StatusBar.Message);
    }

    [Fact]
    public async Task RunsTree_GroupsRunsByDate_AndAddsSnapshotBuckets()
    {
        var treeRows = new[]
        {
            new RunTreeQueryRow
            {
                Id = 21,
                StartedAtUtc = new DateTime(2026, 3, 24, 10, 0, 0, DateTimeKind.Utc),
                FinishedAtUtc = new DateTime(2026, 3, 24, 10, 11, 0, DateTimeKind.Utc),
                Status = "ok",
                ItemsCount = 5,
                SuccessfulSnapshotsCount = 4,
                FailedSnapshotsCount = 1
            },
            new RunTreeQueryRow
            {
                Id = 18,
                StartedAtUtc = new DateTime(2026, 3, 24, 8, 45, 0, DateTimeKind.Utc),
                FinishedAtUtc = new DateTime(2026, 3, 24, 8, 57, 0, DateTimeKind.Utc),
                Status = "error",
                ItemsCount = 3,
                SuccessfulSnapshotsCount = 1,
                FailedSnapshotsCount = 2
            },
            new RunTreeQueryRow
            {
                Id = 14,
                StartedAtUtc = new DateTime(2026, 3, 23, 17, 30, 0, DateTimeKind.Utc),
                FinishedAtUtc = new DateTime(2026, 3, 23, 17, 43, 0, DateTimeKind.Utc),
                Status = "ok",
                ItemsCount = 2,
                SuccessfulSnapshotsCount = 2,
                FailedSnapshotsCount = 0
            }
        };

        var sut = CreateController(
            new CrawlerRunResult(7, "ok", 12, 0, "processed=12, errors=0"),
            treeRows: treeRows);

        var result = await sut.RunsTree(CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var nodes = Assert.IsAssignableFrom<IEnumerable<RunTreeNodeVm>>(json.Value).ToList();

        var rootNode = nodes.First();
        Assert.Equal("date:2026-03-24", rootNode.Id);
        Assert.Equal("24.03.2026 (2)", rootNode.Title);
        Assert.Equal(SnapshotScopes.None, rootNode.SnapshotScope);

        var runNode = Assert.Single(nodes.Where(node => node.Id == "run:21"));
        Assert.Equal("run", runNode.NodeType);
        Assert.Equal(21, runNode.RunId);
        Assert.Equal(SnapshotScopes.All, runNode.SnapshotScope);
        Assert.Equal(5, runNode.ItemsCount);

        var successfulNode = Assert.Single(nodes.Where(node => node.Id == "run:21:successful"));
        Assert.Equal("Successful snapshots (4)", successfulNode.Title);
        Assert.Equal(SnapshotScopes.Successful, successfulNode.SnapshotScope);

        var failedNode = Assert.Single(nodes.Where(node => node.Id == "run:18:failed"));
        Assert.Equal("Failed snapshots (2)", failedNode.Title);
        Assert.Equal(SnapshotScopes.Failed, failedNode.SnapshotScope);
    }

    [Fact]
    public async Task SnapshotsGrid_WhenSuccessfulScopeSelected_ReturnsOnlySuccessfulSnapshots()
    {
        var snapshotRows = new[]
        {
            new SnapshotGridQueryRow
            {
                Id = 101,
                CapturedAtUtc = new DateTime(2026, 3, 24, 11, 0, 0, DateTimeKind.Utc),
                City = "Kyiv",
                FinalPrice = 19.9m,
                RegularPrice = 24.5m,
                DiscountPercent = 19,
                PromoFlag = true,
                InStock = true,
                IsSuccessful = true
            },
            new SnapshotGridQueryRow
            {
                Id = 102,
                CapturedAtUtc = new DateTime(2026, 3, 24, 11, 5, 0, DateTimeKind.Utc),
                City = "Lviv",
                FinalPrice = 22.4m,
                RegularPrice = 22.4m,
                DiscountPercent = 0,
                PromoFlag = false,
                InStock = true,
                IsSuccessful = false
            }
        };

        var sut = CreateController(
            new CrawlerRunResult(7, "ok", 12, 0, "processed=12, errors=0"),
            snapshotRows: snapshotRows);

        var result = await sut.SnapshotsGrid(7, SnapshotScopes.Successful, new DataSourceRequest(),
            CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var dataSource = Assert.IsType<DataSourceResult>(json.Value);
        var rows = Assert.IsAssignableFrom<IEnumerable>(dataSource.Data).Cast<SnapshotGridRowDto>().ToList();

        var row = Assert.Single(rows);
        Assert.Equal(101, row.Id);
        Assert.Equal("OK", row.Status);
    }

    [Fact]
    public async Task SnapshotsGrid_WhenFailedScopeSelected_ReturnsOnlyFailedSnapshots()
    {
        var snapshotRows = new[]
        {
            new SnapshotGridQueryRow
            {
                Id = 101,
                CapturedAtUtc = new DateTime(2026, 3, 24, 11, 0, 0, DateTimeKind.Utc),
                City = "Kyiv",
                FinalPrice = 19.9m,
                RegularPrice = 24.5m,
                DiscountPercent = 19,
                PromoFlag = true,
                InStock = true,
                IsSuccessful = true
            },
            new SnapshotGridQueryRow
            {
                Id = 102,
                CapturedAtUtc = new DateTime(2026, 3, 24, 11, 5, 0, DateTimeKind.Utc),
                City = "Lviv",
                FinalPrice = 22.4m,
                RegularPrice = 22.4m,
                DiscountPercent = 0,
                PromoFlag = false,
                InStock = true,
                IsSuccessful = false
            }
        };

        var sut = CreateController(
            new CrawlerRunResult(7, "ok", 12, 0, "processed=12, errors=0"),
            snapshotRows: snapshotRows);

        var result = await sut.SnapshotsGrid(7, SnapshotScopes.Failed, new DataSourceRequest(), CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var dataSource = Assert.IsType<DataSourceResult>(json.Value);
        var rows = Assert.IsAssignableFrom<IEnumerable>(dataSource.Data).Cast<SnapshotGridRowDto>().ToList();

        var row = Assert.Single(rows);
        Assert.Equal(102, row.Id);
        Assert.Equal("Failed", row.Status);
    }

    private static RunsController CreateController(
        CrawlerRunResult crawlerResponse,
        IEnumerable<RunTreeQueryRow>? treeRows = null,
        IEnumerable<SnapshotGridQueryRow>? snapshotRows = null)
    {
        return new RunsController(
            new EmptyRunsGridQuerySource(),
            new StubRunsTreeQuerySource(treeRows ?? Array.Empty<RunTreeQueryRow>()),
            new StubSnapshotsGridQuerySource(snapshotRows ?? Array.Empty<SnapshotGridQueryRow>()),
            new EmptyProductsGridQuerySource(),
            new FakeRunCrawlerUseCase(crawlerResponse),
            NullLogger<RunsController>.Instance);
    }

    private sealed class FakeRunCrawlerUseCase(CrawlerRunResult response) : IRunCrawlerUseCase
    {
        public Task<CrawlerRunResult> RunVegetablesAsync(CancellationToken ct) => Task.FromResult(response);
    }

    private sealed class EmptyRunsGridQuerySource : IRunsGridQuerySource
    {
        public IQueryable<RunGridQueryRow> Build() => Array.Empty<RunGridQueryRow>().AsQueryable();
    }

    private sealed class StubRunsTreeQuerySource(IEnumerable<RunTreeQueryRow> rows) : IRunsTreeQuerySource
    {
        public IQueryable<RunTreeQueryRow> Build() => rows.AsQueryable();
    }

    private sealed class StubSnapshotsGridQuerySource(IEnumerable<SnapshotGridQueryRow> rows)
        : ISnapshotsGridQuerySource
    {
        public IQueryable<SnapshotGridQueryRow> Build(long runId) => rows.AsQueryable();
    }

    private sealed class EmptyProductsGridQuerySource : IProductsGridQuerySource
    {
        public IQueryable<ProductGridQueryRow> Build(long snapshotId) =>
            Array.Empty<ProductGridQueryRow>().AsQueryable();
    }
}
