using PriceCrawler.Application.Models;

namespace PriceCrawler.Application.Abstractions;

public interface ICrawlerProgressReporter
{
    void Reset();

    void SetTotalDiscovered(int value);

    void SetNewProducts(int value);

    void SetUpdatedProducts(int value);

    void SetSelectedForCheck(int value);

    void SetProductQueueTotal(int value);

    void IncrementProductQueueTotal(int value);

    void IncrementProductProcessed();

    void IncrementProductSucceeded();

    void IncrementProductFailed();

    void SetListingQueueTotal(int value);

    void IncrementListingQueueTotal(int value);

    void IncrementListingProcessed();

    void IncrementListingSucceeded();

    void IncrementListingFailed();

    void IncrementProductLinksDiscoveredFromListings(int value);

    void IncrementProductLinksEnqueuedFromListings(int value);

    void SetCurrentStage(string stage);

    void SetCurrentItem(string item);

    void SetDiscoveryProgress(
        int processedSeeds,
        int totalSeeds,
        int discoveredProductUrls,
        string currentSeedName,
        string currentSeedUrl,
        int currentPageNumber);

    CrawlerProgressSnapshot GetSnapshot();
}
