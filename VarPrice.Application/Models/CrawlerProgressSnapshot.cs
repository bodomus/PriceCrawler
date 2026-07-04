namespace VarPrice.Application.Models;

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
    int CurrentDiscoveryPageNumber = 0);
