using VarPrice.Application.UseCases;
using VarPrice.Domain.Constants;

namespace VarPrice.Web.Tests;

public sealed class CrawlerRunMetricsTests
{
    [Fact]
    public void NewInstance_AllCountersAreZero()
        => Assert.Equal(0, new CrawlerRunMetrics().Snapshot().SucceededCount);

    [Fact]
    public async Task ConcurrentIncrements_AreNotLost()
    {
        var metrics = new CrawlerRunMetrics();
        await Parallel.ForEachAsync(Enumerable.Range(0, 1_000), async (_, _) =>
        {
            metrics.RecordObservation(productCreated: true, snapshotCreated: true, errorCreated: true);
            await Task.Yield();
        });
        var result = metrics.Snapshot();
        Assert.Equal(1_000, result.ProductsCreatedCount);
        Assert.Equal(1_000, result.SnapshotsCreatedCount);
        Assert.Equal(1_000, result.ErrorsCreatedCount);
    }

    [Fact]
    public void NegativeValues_AreRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new CrawlerRunMetrics().SetQueue(-1, 0, 0));

    [Fact]
    public void DuplicateStageCompletion_IsRejected()
    {
        var metrics = new CrawlerRunMetrics();
        metrics.AddStage(CrawlerRunStages.Discovery, 1);
        Assert.Throws<InvalidOperationException>(() => metrics.AddStage(CrawlerRunStages.Discovery, 2));
    }
}
