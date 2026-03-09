using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using VarPrice.Application.Grids.Runs;
using VarPrice.Application.Grids.Runs.QueryRows;
using VarPrice.Application.Models;
using VarPrice.Web.Controllers;
using VarPrice.Web.Crawler;
using VarPrice.Web.Storage.Db;
using VarPrice.Web.ViewModels.Runs;

namespace VarPrice.Web.Tests;

public sealed class RunsControllerTests
{
    [Fact]
    public void Index_ReturnsDashboardViewModel()
    {
        var sut = CreateController(
            DbResult<CrawlerRunResult>.Success(new CrawlerRunResult(7, "ok", 12, 0, "processed=12, errors=0")));

        var result = sut.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<RunsDashboardVm>(view.Model);
        Assert.Equal("VARUS - Dashboard", model.PageTitle);
        Assert.False(string.IsNullOrWhiteSpace(model.AppVersion));
    }

    [Fact]
    public async Task IngestVegetables_WhenDbFailsInProduction_ShowsSafeStatusMessage()
    {
        var error = new DbError(
            "connection",
            "Database is unavailable.",
            "Host=localhost;Password=secret",
            "PgCrawlerRepository.StartRunAsync",
            "ABC123");
        var sut = CreateController(DbResult<CrawlerRunResult>.Fail(error));

        var result = await sut.IngestVegetables(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<RunsDashboardVm>(view.Model);
        Assert.NotNull(model.StatusBar);
        Assert.Equal("error", model.StatusBar!.Level);
        Assert.Contains("Database operation failed: Database is unavailable.", model.StatusBar.Message);
        Assert.Contains("CorrelationId: ABC123", model.StatusBar.Message);
        Assert.DoesNotContain("Host=localhost", model.StatusBar.Message);
    }

    [Fact]
    public async Task IngestVegetables_WhenDbFailsInDevelopment_IncludesTechnicalDetails()
    {
        var error = new DbError(
            "connection",
            "Database is unavailable.",
            "Host=localhost;Password=secret",
            "PgCrawlerRepository.StartRunAsync",
            "ABC123");
        var sut = CreateController(
            DbResult<CrawlerRunResult>.Fail(error),
            new FakeWebHostEnvironment { EnvironmentName = Environments.Development });

        var result = await sut.IngestVegetables(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<RunsDashboardVm>(view.Model);
        Assert.NotNull(model.StatusBar);
        Assert.Contains("Details: Host=localhost;Password=secret", model.StatusBar!.Message);
    }

    [Fact]
    public async Task IngestVegetables_WhenRunSucceeds_ShowsLatestRunSummary()
    {
        var run = new CrawlerRunResult(17, "ok", 24, 1, "processed=24, errors=1");
        var sut = CreateController(DbResult<CrawlerRunResult>.Success(run));

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
        DbResult<CrawlerRunResult> crawlerResponse,
        IWebHostEnvironment? environment = null)
    {
        return new RunsController(
            new EmptyRunsGridQuerySource(),
            new EmptySnapshotsGridQuerySource(),
            new EmptyProductsGridQuerySource(),
            new FakeCrawlerRunner(crawlerResponse),
            environment ?? new FakeWebHostEnvironment(),
            NullLogger<RunsController>.Instance);
    }

    private sealed class FakeCrawlerRunner(DbResult<CrawlerRunResult> response) : ICrawlerRunner
    {
        public Task<DbResult<CrawlerRunResult>> RunVegetablesAsync(CancellationToken ct) => Task.FromResult(response);
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

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "VarPrice.Web.Tests";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public string EnvironmentName { get; set; } = Environments.Production;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
    }
}
