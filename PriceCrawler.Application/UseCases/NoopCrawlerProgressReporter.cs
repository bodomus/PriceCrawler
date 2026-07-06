using PriceCrawler.Application.Abstractions;
using PriceCrawler.Application.Models;

namespace PriceCrawler.Application.UseCases;

internal sealed class NoopCrawlerProgressReporter : ICrawlerProgressReporter
{
    private static readonly CrawlerProgressSnapshot
        EmptySnapshot = new(0, 0, 0, 0, 0, 0, 0, string.Empty, string.Empty);

    public void Reset()
    {
    }

    public void SetTotalDiscovered(int value)
    {
    }

    public void SetNewProducts(int value)
    {
    }

    public void SetUpdatedProducts(int value)
    {
    }

    public void SetSelectedForCheck(int value)
    {
    }

    public void SetProductQueueTotal(int value)
    {
    }

    public void IncrementProductQueueTotal(int value)
    {
    }

    public void IncrementProductProcessed()
    {
    }

    public void IncrementProductSucceeded()
    {
    }

    public void IncrementProductFailed()
    {
    }

    public void SetListingQueueTotal(int value)
    {
    }

    public void IncrementListingQueueTotal(int value)
    {
    }

    public void IncrementListingProcessed()
    {
    }

    public void IncrementListingSucceeded()
    {
    }

    public void IncrementListingFailed()
    {
    }

    public void IncrementProductLinksDiscoveredFromListings(int value)
    {
    }

    public void IncrementProductLinksEnqueuedFromListings(int value)
    {
    }

    public void SetCurrentStage(string stage)
    {
    }

    public void SetCurrentItem(string item)
    {
    }

    public void SetDiscoveryProgress(
        int processedSeeds,
        int totalSeeds,
        int discoveredProductUrls,
        string currentSeedName,
        string currentSeedUrl,
        int currentPageNumber)
    {
    }

    public CrawlerProgressSnapshot GetSnapshot() => EmptySnapshot;
}
