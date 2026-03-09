using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Grids.Runs;
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

    private static RunsController CreateController(
        CrawlerRunResult crawlerResponse)
    {
        return new RunsController(
            new EmptyRunsGridQuerySource(),
            new EmptySnapshotsGridQuerySource(),
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

    private sealed class EmptySnapshotsGridQuerySource : ISnapshotsGridQuerySource
    {
        public IQueryable<SnapshotGridQueryRow> Build(long runId) => Array.Empty<SnapshotGridQueryRow>().AsQueryable();
    }

    private sealed class EmptyProductsGridQuerySource : IProductsGridQuerySource
    {
        public IQueryable<ProductGridQueryRow> Build(long snapshotId) =>
            Array.Empty<ProductGridQueryRow>().AsQueryable();
    }
}
