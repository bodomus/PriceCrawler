using PriceCrawler.Application.Abstractions;

namespace PriceCrawler.Application.Models;

public sealed class CrawlerProgressState : ICrawlerProgressReporter
{
    private readonly object _textLock = new();
    private int _totalDiscovered;
    private int _newProducts;
    private int _updatedProducts;
    private int _selectedForCheck;
    private int _productQueueTotal;
    private int _productProcessed;
    private int _productSucceeded;
    private int _productFailed;
    private int _listingQueueTotal;
    private int _listingProcessed;
    private int _listingSucceeded;
    private int _listingFailed;
    private int _productLinksDiscoveredFromListings;
    private int _productLinksEnqueuedFromListings;
    private int _discoveryProcessedSeeds;
    private int _discoveryTotalSeeds;
    private int _discoveryDiscoveredProductUrls;
    private int _currentDiscoveryPageNumber;
    private string _currentStage = string.Empty;
    private string _currentItem = string.Empty;
    private string _currentDiscoverySeedName = string.Empty;
    private string _currentDiscoverySeedUrl = string.Empty;

    public void Reset()
    {
        Volatile.Write(ref _totalDiscovered, 0);
        Volatile.Write(ref _newProducts, 0);
        Volatile.Write(ref _updatedProducts, 0);
        Volatile.Write(ref _selectedForCheck, 0);
        Volatile.Write(ref _productQueueTotal, 0);
        Volatile.Write(ref _productProcessed, 0);
        Volatile.Write(ref _productSucceeded, 0);
        Volatile.Write(ref _productFailed, 0);
        Volatile.Write(ref _listingQueueTotal, 0);
        Volatile.Write(ref _listingProcessed, 0);
        Volatile.Write(ref _listingSucceeded, 0);
        Volatile.Write(ref _listingFailed, 0);
        Volatile.Write(ref _productLinksDiscoveredFromListings, 0);
        Volatile.Write(ref _productLinksEnqueuedFromListings, 0);
        Volatile.Write(ref _discoveryProcessedSeeds, 0);
        Volatile.Write(ref _discoveryTotalSeeds, 0);
        Volatile.Write(ref _discoveryDiscoveredProductUrls, 0);
        Volatile.Write(ref _currentDiscoveryPageNumber, 0);

        lock (_textLock)
        {
            _currentStage = string.Empty;
            _currentItem = string.Empty;
            _currentDiscoverySeedName = string.Empty;
            _currentDiscoverySeedUrl = string.Empty;
        }
    }

    public void SetTotalDiscovered(int value) => Volatile.Write(ref _totalDiscovered, Normalize(value));

    public void SetNewProducts(int value) => Volatile.Write(ref _newProducts, Normalize(value));

    public void SetUpdatedProducts(int value) => Volatile.Write(ref _updatedProducts, Normalize(value));

    public void SetSelectedForCheck(int value) => Volatile.Write(ref _selectedForCheck, Normalize(value));

    public void SetProductQueueTotal(int value) => Volatile.Write(ref _productQueueTotal, Normalize(value));

    public void IncrementProductQueueTotal(int value)
    {
        var normalized = Normalize(value);
        if (normalized == 0)
        {
            return;
        }

        Interlocked.Add(ref _productQueueTotal, normalized);
    }

    public void IncrementProductProcessed() => Interlocked.Increment(ref _productProcessed);

    public void IncrementProductSucceeded() => Interlocked.Increment(ref _productSucceeded);

    public void IncrementProductFailed() => Interlocked.Increment(ref _productFailed);

    public void SetListingQueueTotal(int value) => Volatile.Write(ref _listingQueueTotal, Normalize(value));

    public void IncrementListingQueueTotal(int value)
    {
        var normalized = Normalize(value);
        if (normalized == 0)
        {
            return;
        }

        Interlocked.Add(ref _listingQueueTotal, normalized);
    }

    public void IncrementListingProcessed() => Interlocked.Increment(ref _listingProcessed);

    public void IncrementListingSucceeded() => Interlocked.Increment(ref _listingSucceeded);

    public void IncrementListingFailed() => Interlocked.Increment(ref _listingFailed);

    public void IncrementProductLinksDiscoveredFromListings(int value)
    {
        var normalized = Normalize(value);
        if (normalized == 0)
        {
            return;
        }

        Interlocked.Add(ref _productLinksDiscoveredFromListings, normalized);
    }

    public void IncrementProductLinksEnqueuedFromListings(int value)
    {
        var normalized = Normalize(value);
        if (normalized == 0)
        {
            return;
        }

        Interlocked.Add(ref _productLinksEnqueuedFromListings, normalized);
    }

    public void SetCurrentStage(string stage)
    {
        lock (_textLock)
        {
            _currentStage = stage.Trim();
        }
    }

    public void SetCurrentItem(string item)
    {
        lock (_textLock)
        {
            _currentItem = item.Trim();
        }
    }

    public void SetDiscoveryProgress(
        int processedSeeds,
        int totalSeeds,
        int discoveredProductUrls,
        string currentSeedName,
        string currentSeedUrl,
        int currentPageNumber)
    {
        Volatile.Write(ref _discoveryProcessedSeeds, Normalize(processedSeeds));
        Volatile.Write(ref _discoveryTotalSeeds, Normalize(totalSeeds));
        Volatile.Write(ref _discoveryDiscoveredProductUrls, Normalize(discoveredProductUrls));
        Volatile.Write(ref _totalDiscovered, Normalize(discoveredProductUrls));
        Volatile.Write(ref _currentDiscoveryPageNumber, Normalize(currentPageNumber));

        lock (_textLock)
        {
            _currentDiscoverySeedName = currentSeedName.Trim();
            _currentDiscoverySeedUrl = currentSeedUrl.Trim();
            _currentItem = FormatDiscoveryCurrentItem(
                _currentDiscoverySeedName,
                _currentDiscoverySeedUrl,
                Volatile.Read(ref _currentDiscoveryPageNumber));
        }
    }

    public CrawlerProgressSnapshot GetSnapshot()
    {
        lock (_textLock)
        {
            return new CrawlerProgressSnapshot(
                Volatile.Read(ref _totalDiscovered),
                Volatile.Read(ref _newProducts),
                Volatile.Read(ref _updatedProducts),
                Volatile.Read(ref _selectedForCheck),
                Volatile.Read(ref _productProcessed),
                Volatile.Read(ref _productSucceeded),
                Volatile.Read(ref _productFailed),
                _currentStage,
                _currentItem,
                Volatile.Read(ref _productQueueTotal),
                Volatile.Read(ref _discoveryProcessedSeeds),
                Volatile.Read(ref _discoveryTotalSeeds),
                Volatile.Read(ref _discoveryDiscoveredProductUrls),
                _currentDiscoverySeedName,
                _currentDiscoverySeedUrl,
                Volatile.Read(ref _currentDiscoveryPageNumber),
                Volatile.Read(ref _productQueueTotal),
                Volatile.Read(ref _productProcessed),
                Volatile.Read(ref _productSucceeded),
                Volatile.Read(ref _productFailed),
                Volatile.Read(ref _listingQueueTotal),
                Volatile.Read(ref _listingProcessed),
                Volatile.Read(ref _listingSucceeded),
                Volatile.Read(ref _listingFailed),
                Volatile.Read(ref _productLinksDiscoveredFromListings),
                Volatile.Read(ref _productLinksEnqueuedFromListings));
        }
    }

    private static int Normalize(int value) => Math.Max(0, value);

    private static string FormatDiscoveryCurrentItem(string seedName, string seedUrl, int pageNumber)
    {
        var page = pageNumber <= 0 ? "-" : pageNumber.ToString();

        if (string.IsNullOrWhiteSpace(seedName) && string.IsNullOrWhiteSpace(seedUrl))
        {
            return pageNumber <= 0 ? string.Empty : $"page {page}";
        }

        if (string.IsNullOrWhiteSpace(seedUrl))
        {
            return $"{seedName} | page {page}";
        }

        if (string.IsNullOrWhiteSpace(seedName))
        {
            return $"page {page} | {seedUrl}";
        }

        return $"{seedName} | page {page} | {seedUrl}";
    }
}
