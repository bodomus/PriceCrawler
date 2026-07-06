using PriceCrawler.Application.Models;

namespace PriceCrawler.Web.Tests;

public sealed class CrawlerProgressStateTests
{
    [Fact]
    public void State_StoresCountersAndText()
    {
        var state = new CrawlerProgressState();

        state.SetTotalDiscovered(38421);
        state.SetNewProducts(137);
        state.SetUpdatedProducts(38284);
        state.SetSelectedForCheck(5000);
        state.SetProductQueueTotal(5000);
        state.IncrementProductQueueTotal(12);
        state.IncrementProductProcessed();
        state.IncrementProductSucceeded();
        state.IncrementProductFailed();
        state.SetListingQueueTotal(3);
        state.IncrementListingProcessed();
        state.IncrementListingSucceeded();
        state.IncrementProductLinksDiscoveredFromListings(541);
        state.IncrementProductLinksEnqueuedFromListings(12);
        state.SetCurrentStage(" Проверка товаров ");
        state.SetCurrentItem(" sku-1 ");

        var snapshot = state.GetSnapshot();

        Assert.Equal(38421, snapshot.TotalDiscovered);
        Assert.Equal(137, snapshot.NewProducts);
        Assert.Equal(38284, snapshot.UpdatedProducts);
        Assert.Equal(5000, snapshot.SelectedForCheck);
        Assert.Equal(5012, snapshot.QueueLinksRequested);
        Assert.Equal(1, snapshot.CheckedProducts);
        Assert.Equal(1, snapshot.SuccessfulProducts);
        Assert.Equal(1, snapshot.FailedProducts);
        Assert.Equal(5012, snapshot.ProductQueueTotal);
        Assert.Equal(1, snapshot.ProductProcessed);
        Assert.Equal(1, snapshot.ProductSucceeded);
        Assert.Equal(1, snapshot.ProductFailed);
        Assert.Equal(3, snapshot.ListingQueueTotal);
        Assert.Equal(1, snapshot.ListingProcessed);
        Assert.Equal(1, snapshot.ListingSucceeded);
        Assert.Equal(0, snapshot.ListingFailed);
        Assert.Equal(541, snapshot.ProductLinksDiscoveredFromListings);
        Assert.Equal(12, snapshot.ProductLinksEnqueuedFromListings);
        Assert.Equal("Проверка товаров", snapshot.CurrentStage);
        Assert.Equal("sku-1", snapshot.CurrentItem);
    }

    [Fact]
    public async Task State_IncrementsCountersThreadSafely()
    {
        var state = new CrawlerProgressState();
        const int workers = 16;
        const int iterations = 1000;

        await Task.WhenAll(Enumerable.Range(0, workers).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++)
            {
                state.IncrementProductProcessed();
                state.IncrementProductSucceeded();
                state.IncrementProductFailed();
                state.IncrementListingProcessed();
                state.IncrementListingSucceeded();
                state.IncrementListingFailed();
            }
        })));

        var snapshot = state.GetSnapshot();

        Assert.Equal(workers * iterations, snapshot.CheckedProducts);
        Assert.Equal(workers * iterations, snapshot.SuccessfulProducts);
        Assert.Equal(workers * iterations, snapshot.FailedProducts);
        Assert.Equal(workers * iterations, snapshot.ListingProcessed);
        Assert.Equal(workers * iterations, snapshot.ListingSucceeded);
        Assert.Equal(workers * iterations, snapshot.ListingFailed);
    }

    [Fact]
    public void State_StoresDiscoveryProgressAndCurrentItem()
    {
        var state = new CrawlerProgressState();

        state.SetDiscoveryProgress(
            processedSeeds: 2,
            totalSeeds: 5,
            discoveredProductUrls: 137,
            currentSeedName: " Fresh ",
            currentSeedUrl: " https://varus.ua/fresh ",
            currentPageNumber: 3);

        var snapshot = state.GetSnapshot();

        Assert.Equal(137, snapshot.TotalDiscovered);
        Assert.Equal(2, snapshot.DiscoveryProcessedSeeds);
        Assert.Equal(5, snapshot.DiscoveryTotalSeeds);
        Assert.Equal(137, snapshot.DiscoveryDiscoveredProductUrls);
        Assert.Equal("Fresh", snapshot.CurrentDiscoverySeedName);
        Assert.Equal("https://varus.ua/fresh", snapshot.CurrentDiscoverySeedUrl);
        Assert.Equal(3, snapshot.CurrentDiscoveryPageNumber);
        Assert.Equal("Fresh | page 3 | https://varus.ua/fresh", snapshot.CurrentItem);
    }

    [Fact]
    public void Reset_ClearsCountersAndText()
    {
        var state = new CrawlerProgressState();
        state.SetTotalDiscovered(10);
        state.SetProductQueueTotal(5);
        state.IncrementProductProcessed();
        state.SetListingQueueTotal(2);
        state.IncrementListingProcessed();
        state.IncrementProductLinksDiscoveredFromListings(10);
        state.IncrementProductLinksEnqueuedFromListings(3);
        state.SetCurrentStage("stage");
        state.SetCurrentItem("item");

        state.Reset();

        var snapshot = state.GetSnapshot();

        Assert.Equal(0, snapshot.TotalDiscovered);
        Assert.Equal(0, snapshot.QueueLinksRequested);
        Assert.Equal(0, snapshot.CheckedProducts);
        Assert.Equal(0, snapshot.ProductQueueTotal);
        Assert.Equal(0, snapshot.ProductProcessed);
        Assert.Equal(0, snapshot.ProductSucceeded);
        Assert.Equal(0, snapshot.ProductFailed);
        Assert.Equal(0, snapshot.ListingQueueTotal);
        Assert.Equal(0, snapshot.ListingProcessed);
        Assert.Equal(0, snapshot.ListingSucceeded);
        Assert.Equal(0, snapshot.ListingFailed);
        Assert.Equal(0, snapshot.ProductLinksDiscoveredFromListings);
        Assert.Equal(0, snapshot.ProductLinksEnqueuedFromListings);
        Assert.Equal(string.Empty, snapshot.CurrentStage);
        Assert.Equal(string.Empty, snapshot.CurrentItem);
    }

    [Fact]
    public void QueueRequestedLinks_CanGrowWhenListingsDiscoverProducts()
    {
        var state = new CrawlerProgressState();
        state.SetSelectedForCheck(2);
        state.SetProductQueueTotal(2);

        state.IncrementProductQueueTotal(7);

        var snapshot = state.GetSnapshot();

        Assert.Equal(2, snapshot.SelectedForCheck);
        Assert.Equal(9, snapshot.QueueLinksRequested);
        Assert.Equal(9, snapshot.ProductQueueTotal);
    }

    [Theory]
    [InlineData(4100, 5000, 82.0d)]
    [InlineData(1, 0, 0d)]
    [InlineData(0, 10, 0d)]
    [InlineData(12, 10, 100d)]
    public void Formatter_CalculatesPercentWithoutDivisionByZero(int current, int total, double expected)
    {
        var actual = CrawlerProgressFormatter.CalculatePercent(current, total);

        Assert.Equal(expected, actual, precision: 1);
    }

    [Theory]
    [InlineData(4041, 5000, "4 041 / 5 000")]
    [InlineData(38, 0, "38")]
    [InlineData(0, 0, "-")]
    public void Formatter_FormatsCurrentOverTotal(int current, int total, string expected)
    {
        var actual = CrawlerProgressFormatter.FormatCurrentOverTotal(current, total);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Snapshot_CanRepresentFinalState()
    {
        var state = new CrawlerProgressState();
        state.SetTotalDiscovered(3);
        state.SetSelectedForCheck(3);
        state.SetCurrentStage("Завершено");
        state.SetProductQueueTotal(3);
        state.IncrementProductProcessed();
        state.IncrementProductProcessed();
        state.IncrementProductProcessed();
        state.IncrementProductSucceeded();
        state.IncrementProductSucceeded();
        state.IncrementProductFailed();
        state.SetListingQueueTotal(10);
        state.IncrementListingProcessed();

        var snapshot = state.GetSnapshot();

        Assert.Equal("Завершено", snapshot.CurrentStage);
        Assert.Equal(3, snapshot.CheckedProducts);
        Assert.Equal(2, snapshot.SuccessfulProducts);
        Assert.Equal(1, snapshot.FailedProducts);
        Assert.Equal("100.0%", CrawlerProgressFormatter.FormatPercent(
            CrawlerProgressFormatter.CalculatePercent(snapshot.ProductProcessed, snapshot.ProductQueueTotal)));
    }
}
