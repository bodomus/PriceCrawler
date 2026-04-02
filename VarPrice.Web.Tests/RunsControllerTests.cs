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

using ProductAnalysisService = VarPrice.Infrastructure.Queries.Runs.ProductAnalysisService;

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
                Price = 19.9m,
                OldPrice = 24.5m,
                DiscountPercent = 19,
                PromoFlag = true,
                InStock = true,
                IsSuccessful = true
            },
            new SnapshotGridQueryRow
            {
                Id = 102,
                CapturedAtUtc = new DateTime(2026, 3, 24, 11, 5, 0, DateTimeKind.Utc),
                Price = 22.4m,
                OldPrice = 22.4m,
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
                Price = 19.9m,
                OldPrice = 24.5m,
                DiscountPercent = 19,
                PromoFlag = true,
                InStock = true,
                IsSuccessful = true
            },
            new SnapshotGridQueryRow
            {
                Id = 102,
                CapturedAtUtc = new DateTime(2026, 3, 24, 11, 5, 0, DateTimeKind.Utc),
                Price = 22.4m,
                OldPrice = 22.4m,
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

    [Fact]
    public async Task ProductDetails_WhenSnapshotSelected_ReturnsAnalyticsCardPayload()
    {
        var details = new ProductDetailsQueryRow
        {
            Id = 501,
            SnapshotId = 101,
            RunId = 7,
            ExternalId = "SKU-11",
            Name = "Tomato",
            Url = "https://varus.ua/tomato",
            PackValue = 1,
            PackUnit = "kg",
            CurrentPrice = 49.9m,
            OldPrice = 59.9m,
            DiscountPercent = 16.7m,
            PromoFlag = true,
            InStock = true,
            UpdatedAtUtc = new DateTime(2026, 3, 24, 8, 0, 0, DateTimeKind.Utc),
            CapturedAtUtc = new DateTime(2026, 3, 24, 9, 0, 0, DateTimeKind.Utc),
            Source = "varus"
        };

        var sut = CreateController(
            new CrawlerRunResult(7, "ok", 12, 0, "processed=12, errors=0"),
            productDetails: details);

        var result = await sut.ProductDetails(101, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var dto = Assert.IsType<ProductDetailsDto>(json.Value);
        Assert.Equal(501, dto.Id);
        Assert.Equal("Tomato", dto.Name);
        Assert.Equal("SKU-11", dto.Sku);
        Assert.Equal("1 kg", dto.Unit);
        Assert.Equal(49.9m, dto.CurrentPrice);
        Assert.Equal(7, dto.RunId);
    }

    [Fact]
    public async Task ProductAnalysis_WhenSnapshotSelected_ReturnsUnifiedAnalyticsPayload()
    {
        var details = new ProductDetailsQueryRow
        {
            Id = 501,
            SnapshotId = 101,
            RunId = 7,
            ExternalId = "SKU-11",
            Name = "Tomato",
            Url = "https://varus.ua/tomato",
            PackValue = 1,
            PackUnit = "kg",
            CurrentPrice = 49.9m,
            OldPrice = 59.9m,
            DiscountPercent = 16.7m,
            PromoFlag = true,
            InStock = true,
            UpdatedAtUtc = new DateTime(2026, 3, 24, 8, 0, 0, DateTimeKind.Utc),
            CapturedAtUtc = new DateTime(2026, 3, 24, 9, 0, 0, DateTimeKind.Utc),
            Source = "varus"
        };
        var historyRows = new[]
        {
            new ProductPriceHistoryQueryRow
            {
                Id = 88,
                RunId = 14,
                CapturedAtUtc = new DateTime(2026, 3, 23, 8, 0, 0, DateTimeKind.Utc),
                Price = 21.5m,
                OldPrice = null,
                DiscountPercent = null,
                PromoFlag = false,
                InStock = true,
                Source = "varus"
            },
            new ProductPriceHistoryQueryRow
            {
                Id = 101,
                RunId = 21,
                CapturedAtUtc = new DateTime(2026, 3, 24, 11, 0, 0, DateTimeKind.Utc),
                Price = 19.9m,
                OldPrice = 24.5m,
                DiscountPercent = 19,
                PromoFlag = true,
                InStock = true,
                Source = "varus"
            }
        };

        var sut = CreateController(
            new CrawlerRunResult(7, "ok", 12, 0, "processed=12, errors=0"),
            productDetails: details,
            productHistoryRows: historyRows);

        var result = await sut.ProductAnalysis(101, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var dto = Assert.IsType<ProductAnalysisDto>(json.Value);

        Assert.Equal(101, dto.SnapshotId);
        Assert.NotNull(dto.ProductCard);
        Assert.Equal("Tomato", dto.ProductCard!.Name);
        Assert.Equal(2, dto.History.Count);
        Assert.Equal(101, dto.Analytics.SnapshotId);
        Assert.Equal(2, dto.Analytics.HistoryPointsCount);
    }

    [Fact]
    public async Task ProductHistory_WhenSnapshotSelected_ReturnsHistoryRows()
    {
        var historyRows = new[]
        {
            new ProductPriceHistoryQueryRow
            {
                Id = 101,
                RunId = 21,
                CapturedAtUtc = new DateTime(2026, 3, 24, 11, 0, 0, DateTimeKind.Utc),
                Price = 19.9m,
                OldPrice = 24.5m,
                DiscountPercent = 19,
                PromoFlag = true,
                InStock = true,
                Source = "varus"
            },
            new ProductPriceHistoryQueryRow
            {
                Id = 88,
                RunId = 14,
                CapturedAtUtc = new DateTime(2026, 3, 23, 8, 0, 0, DateTimeKind.Utc),
                Price = 21.5m,
                OldPrice = null,
                DiscountPercent = null,
                PromoFlag = false,
                InStock = true,
                Source = "varus"
            }
        };

        var sut = CreateController(
            new CrawlerRunResult(7, "ok", 12, 0, "processed=12, errors=0"),
            productHistoryRows: historyRows);

        var result = await sut.ProductHistory(101, new DataSourceRequest(), CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var dataSource = Assert.IsType<DataSourceResult>(json.Value);
        var rows = Assert.IsAssignableFrom<IEnumerable>(dataSource.Data).Cast<ProductPriceHistoryRowDto>().ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(101, rows[0].Id);
        Assert.Equal(21, rows[0].RunId);
        Assert.Equal("varus", rows[0].Source);
    }

    [Fact]
    public async Task ProductAnalytics_WhenSnapshotSelected_ReturnsChartSummaryAndSeries()
    {
        var details = new ProductDetailsQueryRow
        {
            Id = 501,
            SnapshotId = 101,
            RunId = 21,
            ExternalId = "SKU-11",
            Name = "Tomato",
            Url = "https://varus.ua/tomato"
        };
        var historyRows = new[]
        {
            new ProductPriceHistoryQueryRow
            {
                Id = 88,
                RunId = 14,
                CapturedAtUtc = new DateTime(2026, 3, 23, 8, 0, 0, DateTimeKind.Utc),
                Price = 21.5m,
                OldPrice = null,
                DiscountPercent = null,
                PromoFlag = false,
                InStock = true,
                Source = "varus"
            },
            new ProductPriceHistoryQueryRow
            {
                Id = 101,
                RunId = 21,
                CapturedAtUtc = new DateTime(2026, 3, 24, 11, 0, 0, DateTimeKind.Utc),
                Price = 19.9m,
                OldPrice = 24.5m,
                DiscountPercent = 19,
                PromoFlag = true,
                InStock = true,
                Source = "varus"
            },
            new ProductPriceHistoryQueryRow
            {
                Id = 133,
                RunId = 27,
                CapturedAtUtc = new DateTime(2026, 3, 25, 9, 30, 0, DateTimeKind.Utc),
                Price = 22.1m,
                OldPrice = 22.1m,
                DiscountPercent = 0,
                PromoFlag = false,
                InStock = false,
                Source = "varus"
            }
        };

        var sut = CreateController(
            new CrawlerRunResult(7, "ok", 12, 0, "processed=12, errors=0"),
            productDetails: details,
            productHistoryRows: historyRows);

        var result = await sut.ProductAnalytics(101, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var dto = Assert.IsType<ProductAnalyticsDto>(json.Value);

        Assert.Equal(101, dto.SnapshotId);
        Assert.Equal(3, dto.HistoryPointsCount);
        Assert.Equal(3, dto.PricePointsCount);
        Assert.Equal(1, dto.PromoMomentsCount);
        Assert.Equal(2, dto.InStockMomentsCount);
        Assert.Equal(19.9m, dto.SelectedPrice);
        Assert.Equal(21.5m, dto.PreviousPrice);
        Assert.Equal(21.5m, dto.FirstObservedPrice);
        Assert.Equal(22.1m, dto.LatestObservedPrice);
        Assert.Equal(19.9m, dto.MinPrice);
        Assert.Equal(22.1m, dto.MaxPrice);
        Assert.Equal(21.17m, dto.AveragePrice);
        Assert.Equal(-1.6m, dto.ChangeFromPreviousAmount);
        Assert.Equal(-7.4m, dto.ChangeFromPreviousPercent);
        Assert.Equal(-1.6m, dto.ChangeFromFirstAmount);
        Assert.Equal(-7.4m, dto.ChangeFromFirstPercent);
        Assert.Equal(3, dto.Points.Count);
        Assert.Equal(88, dto.Points[0].SnapshotId);
        Assert.Equal(101, dto.Points[1].SnapshotId);
    }

    [Fact]
    public async Task RefreshLiveProduct_WhenExtractorSucceeds_ReturnsManualLivePayload()
    {
        var productDetails = new ProductDetailsQueryRow
        {
            Id = 501,
            SnapshotId = 101,
            RunId = 7,
            ExternalId = "SKU-11",
            Name = "Tomato",
            Url = "https://varus.ua/tomato",
            Slug = "tomato",
            PackValue = 1,
            PackUnit = "kg",
            CurrentPrice = 49.9m,
            OldPrice = 59.9m,
            PromoFlag = true,
            InStock = true
        };

        var liveResult = ProductExtractResult.Success(
            new ProductCard("SKU-11", "Tomato Premium", "https://varus.ua/tomato", "tomato", 47.9m, 59.9m, true, true,
                1,
                "kg"),
            200,
            812,
            1.37);

        var sut = CreateController(
            new CrawlerRunResult(7, "ok", 12, 0, "processed=12, errors=0"),
            productDetails: productDetails,
            liveExtractResult: liveResult);

        var result = await sut.RefreshLiveProduct(101, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var dto = Assert.IsType<ProductLiveResultDto>(json.Value);

        Assert.Equal(101, dto.SnapshotId);
        Assert.Equal("success", dto.Status);
        Assert.Equal(200, dto.HttpStatus);
        Assert.Equal(812, dto.LatencyMs);
        Assert.Equal(1.37d, dto.ApproximateRps);
        Assert.NotNull(dto.LiveCard);
        Assert.Equal("Tomato Premium", dto.LiveCard!.Name);
        Assert.Equal(47.9m, dto.LiveCard.CurrentPrice);
        Assert.Null(dto.Issue);
    }

    [Fact]
    public async Task RefreshLiveProduct_WhenExtractorFails_ReturnsErrorPayload()
    {
        var productDetails = new ProductDetailsQueryRow
        {
            Id = 501,
            SnapshotId = 101,
            RunId = 7,
            ExternalId = "SKU-11",
            Name = "Tomato",
            Url = "https://varus.ua/tomato"
        };

        var liveResult = ProductExtractResult.Fail(
            "timeout",
            504,
            "Request timed out after 15s",
            15000,
            0.8,
            true);

        var sut = CreateController(
            new CrawlerRunResult(7, "ok", 12, 0, "processed=12, errors=0"),
            productDetails: productDetails,
            liveExtractResult: liveResult);

        var result = await sut.RefreshLiveProduct(101, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var dto = Assert.IsType<ProductLiveResultDto>(json.Value);

        Assert.Equal("error", dto.Status);
        Assert.Null(dto.LiveCard);
        Assert.NotNull(dto.Issue);
        Assert.Equal("timeout", dto.Issue!.ErrorCode);
        Assert.True(dto.Issue.IsTransient);
        Assert.Equal(504, dto.HttpStatus);
    }

    private static RunsController CreateController(
        CrawlerRunResult crawlerResponse,
        IEnumerable<RunTreeQueryRow>? treeRows = null,
        IEnumerable<SnapshotGridQueryRow>? snapshotRows = null,
        ProductDetailsQueryRow? productDetails = null,
        IEnumerable<ProductPriceHistoryQueryRow>? productHistoryRows = null,
        ProductExtractResult? liveExtractResult = null)
    {
        return new RunsController(
            new EmptyRunsGridQuerySource(),
            new StubRunsTreeQuerySource(treeRows ?? Array.Empty<RunTreeQueryRow>()),
            new StubSnapshotsGridQuerySource(snapshotRows ?? Array.Empty<SnapshotGridQueryRow>()),
            new EmptyProductsGridQuerySource(),
            new ProductAnalysisService(
                new StubProductDetailsQuerySource(productDetails),
                new StubProductPriceHistoryQuerySource(productHistoryRows ??
                                                       Array.Empty<ProductPriceHistoryQueryRow>())),
            new StubProductPriceHistoryQuerySource(productHistoryRows ?? Array.Empty<ProductPriceHistoryQueryRow>()),
            new StubProductCardExtractor(liveExtractResult),
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

    private sealed class StubProductDetailsQuerySource(ProductDetailsQueryRow? row) : IProductDetailsQuerySource
    {
        public IQueryable<ProductDetailsQueryRow> Build(long snapshotId) =>
            row is null ? Array.Empty<ProductDetailsQueryRow>().AsQueryable() : new[] { row }.AsQueryable();
    }

    private sealed class StubProductPriceHistoryQuerySource(IEnumerable<ProductPriceHistoryQueryRow> rows)
        : IProductPriceHistoryQuerySource
    {
        public IQueryable<ProductPriceHistoryQueryRow> Build(long snapshotId) => rows.AsQueryable();
    }

    private sealed class StubProductCardExtractor(ProductExtractResult? result) : IProductCardExtractor
    {
        public Task<ProductExtractResult> ExtractAsync(string url, CancellationToken ct)
        {
            return Task.FromResult(result ?? ProductExtractResult.Fail(
                "not-configured",
                null,
                "Live extractor stub was not configured.",
                0,
                0,
                false));
        }
    }
}
