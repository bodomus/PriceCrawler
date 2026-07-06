namespace PriceCrawler.Application.Models;

public sealed record CrawlerProgressSnapshot(
    int TotalDiscovered,
    int NewProducts,
    int UpdatedProducts,
    int SelectedForCheck,
    int CheckedProducts,
    int SuccessfulProducts,
    int FailedProducts,
    string CurrentStage,
    string CurrentItem,
    int QueueLinksRequested = 0,
    int DiscoveryProcessedSeeds = 0,
    int DiscoveryTotalSeeds = 0,
    int DiscoveryDiscoveredProductUrls = 0,
    string CurrentDiscoverySeedName = "",
    string CurrentDiscoverySeedUrl = "",
    int CurrentDiscoveryPageNumber = 0,
    int ProductQueueTotal = 0,
    int ProductProcessed = 0,
    int ProductSucceeded = 0,
    int ProductFailed = 0,
    int ListingQueueTotal = 0,
    int ListingProcessed = 0,
    int ListingSucceeded = 0,
    int ListingFailed = 0,
    int ProductLinksDiscoveredFromListings = 0,
    int ProductLinksEnqueuedFromListings = 0)
{
    public int TotalQueueItems => ProductQueueTotal + ListingQueueTotal;

    public int TotalProcessedQueueItems => ProductProcessed + ListingProcessed;
}
