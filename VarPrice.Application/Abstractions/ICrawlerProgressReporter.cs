using VarPrice.Application.Models;

namespace VarPrice.Application.Abstractions;

public interface ICrawlerProgressReporter
{
    void SetTotalDiscovered(int value);

    void SetNewProducts(int value);

    void SetUpdatedProducts(int value);

    void SetSelectedForCheck(int value);

    void IncrementChecked();

    void IncrementSuccessful();

    void IncrementFailed();

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
