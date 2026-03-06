using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;
using VarPrice.Application.UseCases;
using VarPrice.Domain.Enums;
using VarPrice.Domain.Interfaces;
using VarPrice.Domain.ValueObjects;

namespace VarPrice.Web.Tests;

public sealed class RunCrawlerUseCaseTests
{
    [Fact]
    public async Task RunVegetablesAsync_Success_TransitionsStatuses()
    {
        var crawlerRepo = new FakeCrawlerRunRepository();
        var ingestionRepo = new FakeIngestionRunRepository();
        var snapshotRepo = new FakePriceSnapshotRepository();
        var source = new FakeSource(["https://example/ovochi/1"]);
        var extractor = new FakeExtractor(ProductExtractResult.Success(
            new ProductCard("1", "name", "url", 10m, null, false, true, null, null, "kyiv"), 200, 10, 1.0d));

        var sut = CreateUseCase(crawlerRepo, ingestionRepo, snapshotRepo, source, extractor);
        var result = await sut.RunVegetablesAsync(CancellationToken.None);

        Assert.Equal("ok", result.Status);
        Assert.Equal(RunStatus.Ok, crawlerRepo.LastStatus);
        Assert.Equal(RunStatus.Ok, ingestionRepo.LastStatus);
        Assert.Null(ingestionRepo.LastError);
    }

    [Fact]
    public async Task RunVegetablesAsync_Failure_SetsErrorInfoInIngestionOnly()
    {
        var crawlerRepo = new FakeCrawlerRunRepository();
        var ingestionRepo = new FakeIngestionRunRepository();
        var snapshotRepo = new FakePriceSnapshotRepository();
        var source = new ThrowingSource();
        var extractor =
            new FakeExtractor(ProductExtractResult.Fail(CrawlerErrorCodes.Unknown, null, "boom", 10, 1.0d, false));

        var sut = CreateUseCase(crawlerRepo, ingestionRepo, snapshotRepo, source, extractor);
        var result = await sut.RunVegetablesAsync(CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Equal(RunStatus.Error, crawlerRepo.LastStatus);
        Assert.Equal(RunStatus.Error, ingestionRepo.LastStatus);
        Assert.NotNull(ingestionRepo.LastError);
    }

    [Fact]
    public async Task RunVegetablesAsync_NotFound_PersistsConcreteErrorCode()
    {
        var crawlerRepo = new FakeCrawlerRunRepository();
        var ingestionRepo = new FakeIngestionRunRepository();
        var snapshotRepo = new FakePriceSnapshotRepository();
        var source = new FakeSource(["https://example/ovochi/missing"]);
        var extractor =
            new FakeExtractor(ProductExtractResult.Fail(CrawlerErrorCodes.NotFound, 404, "HTTP 404", 11, 1.0d, false));

        var sut = CreateUseCase(crawlerRepo, ingestionRepo, snapshotRepo, source, extractor);
        var result = await sut.RunVegetablesAsync(CancellationToken.None);

        Assert.Equal("ok", result.Status);
        Assert.Equal(CrawlerErrorCodes.NotFound, snapshotRepo.LastErrorCode);
        Assert.Equal(404, snapshotRepo.LastHttpStatus);
    }

    private static RunCrawlerUseCase CreateUseCase(
        ICrawlerRunRepository crawler,
        IIngestionRunRepository ingestion,
        IPriceSnapshotRepository snapshot,
        IProductUrlSource source,
        IProductCardExtractor extractor)
    {
        var options = Options.Create(new CrawlerOptions
        {
            SitemapIndexUrl = "https://example/sitemap.xml", VegetablesUrlContains = "ovochi", MaxProductsPerRun = 2
        });
        var filterOptions = Options.Create(new UrlFilterOptions());
        return new RunCrawlerUseCase(options, filterOptions, source, extractor, crawler, ingestion, snapshot,
            NullLogger<RunCrawlerUseCase>.Instance);
    }

    private sealed class FakeSource(IReadOnlyList<string> urls) : IProductUrlSource
    {
        public Task<IReadOnlyList<string>> GetProductUrlsAsync(string sitemapIndexUrl, CancellationToken ct) =>
            Task.FromResult(urls);
    }

    private sealed class ThrowingSource : IProductUrlSource
    {
        public Task<IReadOnlyList<string>> GetProductUrlsAsync(string sitemapIndexUrl, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class FakeExtractor(ProductExtractResult result) : IProductCardExtractor
    {
        public Task<ProductExtractResult> ExtractAsync(string url, CancellationToken ct) => Task.FromResult(result);
    }

    private sealed class FakeCrawlerRunRepository : ICrawlerRunRepository
    {
        public RunStatus LastStatus { get; private set; } = RunStatus.Running;
        public Task<long> StartAsync(string source, CancellationToken ct) => Task.FromResult(1L);

        public Task FinishAsync(long runId, RunStatus status, string? note, CancellationToken ct)
        {
            LastStatus = status;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeIngestionRunRepository : IIngestionRunRepository
    {
        public RunStatus LastStatus { get; private set; } = RunStatus.Running;
        public ErrorInfo? LastError { get; private set; }
        public Task<long> StartAsync(long crawlerRunId, CancellationToken ct) => Task.FromResult(10L);

        public Task FinishAsync(long ingestionRunId, RunStatus status, ErrorInfo? errorInfo, CancellationToken ct)
        {
            LastStatus = status;
            LastError = errorInfo;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePriceSnapshotRepository : IPriceSnapshotRepository
    {
        public string? LastErrorCode { get; private set; }
        public int? LastHttpStatus { get; private set; }

        public Task<long> UpsertProductAsync(string productId, string name, string url, decimal? packValue,
            string? packUnit, CancellationToken ct) => Task.FromResult(5L);

        public Task InsertSnapshotAsync(long runId, long productKey, string? city, decimal price, decimal? oldPrice,
            bool promoFlag, bool? inStock, CancellationToken ct) => Task.CompletedTask;

        public Task InsertProductErrorAsync(long runId, long? productKey, string? city, decimal price,
            decimal? oldPrice, bool promoFlag, bool? inStock, CancellationToken ct) => Task.CompletedTask;

        public Task InsertProductErrorAsync(long runId, string url, string errorCode, int? httpStatus, string? message,
            CancellationToken ct)
        {
            LastErrorCode = errorCode;
            LastHttpStatus = httpStatus;
            return Task.CompletedTask;
        }
    }
}
