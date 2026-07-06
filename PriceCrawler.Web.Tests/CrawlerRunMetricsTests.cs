using PriceCrawler.Application.UseCases;
using PriceCrawler.Domain.Constants;

namespace PriceCrawler.Web.Tests;

public sealed class CrawlerRunMetricsTests
{
    [Fact]
    public void NewInstance_AllCountersAreZero()
        => Assert.Equal(0, new CrawlerRunMetrics().Snapshot().SucceededCount);

    [Fact]
    public async Task ConcurrentIncrements_AreNotLost()
    {
        var metrics = new CrawlerRunMetrics();
        await Parallel.ForEachAsync(Enumerable.Range(0, 1_000), async (index, _) =>
        {
            metrics.RecordObservation(
                productCreated: index % 2 == 0,
                productUpdated: index % 2 != 0,
                snapshotCreated: true,
                errorCreated: true);
            await Task.Yield();
        });
        var result = metrics.Snapshot();
        Assert.Equal(500, result.ProductsCreatedCount);
        Assert.Equal(500, result.ProductsUpdatedCount);
        Assert.Equal(1_000, result.SnapshotsCreatedCount);
        Assert.Equal(1_000, result.ErrorsCreatedCount);
    }

    [Fact]
    public void NegativeValues_AreRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new CrawlerRunMetrics().SetQueue(-1, 0, 0));

    [Fact]
    public void NoProductWrite_DoesNotIncrementCreatedOrUpdated()
    {
        var metrics = new CrawlerRunMetrics();
        metrics.RecordObservation(productCreated: false, productUpdated: false, snapshotCreated: false,
            errorCreated: false);

        Assert.Equal(0, metrics.Snapshot().ProductsCreatedCount);
        Assert.Equal(0, metrics.Snapshot().ProductsUpdatedCount);
    }
}

public sealed class CrawlerRunStageRecorderTests
{
    [Fact]
    public void Snapshot_PreservesInsertionOrder()
    {
        var recorder = new CrawlerRunStageRecorder();
        recorder.Add(CrawlerRunStages.QueueProcessing, 3);
        recorder.Add(CrawlerRunStages.CatalogSelection, 1);
        recorder.Add(CrawlerRunStages.QueueEnqueue, 2);

        Assert.Equal(
            [CrawlerRunStages.QueueProcessing, CrawlerRunStages.CatalogSelection, CrawlerRunStages.QueueEnqueue],
            recorder.Snapshot().Select(x => x.Stage));
    }

    [Fact]
    public void DuplicateStageCompletion_IsRejected()
    {
        var recorder = new CrawlerRunStageRecorder();
        recorder.Add(CrawlerRunStages.Discovery, 1);
        Assert.Throws<InvalidOperationException>(() => recorder.Add(CrawlerRunStages.Discovery, 2));
    }

    [Fact]
    public void UnsupportedStage_IsRejected()
        => Assert.Throws<ArgumentException>(() => new CrawlerRunStageRecorder().Add("unknown", 1));
}
