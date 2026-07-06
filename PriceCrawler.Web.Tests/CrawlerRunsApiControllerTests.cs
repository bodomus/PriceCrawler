using Microsoft.AspNetCore.Mvc;

using PriceCrawler.Domain.Interfaces;
using PriceCrawler.Domain.Models;
using PriceCrawler.Web.Controllers;

namespace PriceCrawler.Web.Tests;

public sealed class CrawlerRunsApiControllerTests
{
    [Fact]
    public async Task GetRecent_InvalidLimit_ReturnsBadRequest()
        => Assert.IsType<BadRequestObjectResult>(await new CrawlerRunsApiController(new FakeRepository())
            .GetRecent(0, null, null));

    [Theory]
    [InlineData("banana", null)]
    [InlineData(null, "finished")]
    public async Task GetRecent_InvalidFilters_ReturnBadRequest(string? runType, string? status)
        => Assert.IsType<BadRequestObjectResult>(await new CrawlerRunsApiController(new FakeRepository())
            .GetRecent(50, runType, status));

    [Fact]
    public async Task GetRecent_ValidFilters_AreNormalized()
    {
        var repository = new FakeRepository();

        Assert.IsType<OkObjectResult>(await new CrawlerRunsApiController(repository)
            .GetRecent(50, " PRICE-COLLECTION ", " OK "));
        Assert.Equal("price-collection", repository.RunType);
        Assert.Equal("ok", repository.Status);
    }

    [Fact]
    public async Task GetById_MissingRun_ReturnsNotFound()
        => Assert.IsType<NotFoundResult>(await new CrawlerRunsApiController(new FakeRepository()).GetById(999));

    [Fact]
    public async Task GetStatistics_InvalidRange_ReturnsBadRequest()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.IsType<BadRequestObjectResult>(await new CrawlerRunsApiController(new FakeRepository())
            .GetStatistics(now, now));
    }

    [Fact]
    public async Task GetStatistics_DefaultRange_IsThirtyDays()
    {
        var repository = new FakeRepository();
        Assert.IsType<OkObjectResult>(await new CrawlerRunsApiController(repository).GetStatistics());
        Assert.InRange((repository.To - repository.From).TotalDays, 29.99, 30.01);
    }

    [Fact]
    public async Task GetStatistics_InvalidRunType_ReturnsBadRequest()
        => Assert.IsType<BadRequestObjectResult>(await new CrawlerRunsApiController(new FakeRepository())
            .GetStatistics(runType: "banana"));

    private sealed class FakeRepository : ICrawlerRunReadRepository
    {
        public DateTimeOffset From { get; private set; }
        public DateTimeOffset To { get; private set; }
        public string? RunType { get; private set; }
        public string? Status { get; private set; }

        public Task<CrawlerRunDetails?> GetByIdAsync(long runId, CancellationToken ct)
            => Task.FromResult<CrawlerRunDetails?>(null);

        public Task<IReadOnlyList<CrawlerRunSummary>> GetRecentAsync(int limit, string? runType, string? status,
            CancellationToken ct)
        {
            RunType = runType;
            Status = status;
            return Task.FromResult<IReadOnlyList<CrawlerRunSummary>>([]);
        }

        public Task<CrawlerRunAggregateStatistics> GetAggregateAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc,
            string? runType, CancellationToken ct)
        {
            From = fromUtc;
            To = toUtc;
            RunType = runType;
            return Task.FromResult(new CrawlerRunAggregateStatistics(fromUtc, toUtc, runType, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 0));
        }
    }
}
