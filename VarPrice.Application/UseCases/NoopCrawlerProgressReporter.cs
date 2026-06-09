using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;

namespace VarPrice.Application.UseCases;

internal sealed class NoopCrawlerProgressReporter : ICrawlerProgressReporter
{
    private static readonly CrawlerProgressSnapshot
        EmptySnapshot = new(0, 0, 0, 0, 0, 0, 0, string.Empty, string.Empty);

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

    public void IncrementChecked()
    {
    }

    public void IncrementSuccessful()
    {
    }

    public void IncrementFailed()
    {
    }

    public void SetCurrentStage(string stage)
    {
    }

    public void SetCurrentItem(string item)
    {
    }

    public CrawlerProgressSnapshot GetSnapshot() => EmptySnapshot;
}
