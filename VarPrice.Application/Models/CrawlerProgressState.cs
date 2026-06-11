using VarPrice.Application.Abstractions;

namespace VarPrice.Application.Models;

public sealed class CrawlerProgressState : ICrawlerProgressReporter
{
    private readonly object _textLock = new();
    private int _totalDiscovered;
    private int _newProducts;
    private int _updatedProducts;
    private int _selectedForCheck;
    private int _checkedProducts;
    private int _successfulProducts;
    private int _failedProducts;
    private string _currentStage = string.Empty;
    private string _currentItem = string.Empty;

    public void SetTotalDiscovered(int value) => Volatile.Write(ref _totalDiscovered, Normalize(value));

    public void SetNewProducts(int value) => Volatile.Write(ref _newProducts, Normalize(value));

    public void SetUpdatedProducts(int value) => Volatile.Write(ref _updatedProducts, Normalize(value));

    public void SetSelectedForCheck(int value) => Volatile.Write(ref _selectedForCheck, Normalize(value));

    public void IncrementChecked() => Interlocked.Increment(ref _checkedProducts);

    public void IncrementSuccessful() => Interlocked.Increment(ref _successfulProducts);

    public void IncrementFailed() => Interlocked.Increment(ref _failedProducts);

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

    public CrawlerProgressSnapshot GetSnapshot()
    {
        lock (_textLock)
        {
            return new CrawlerProgressSnapshot(
                Volatile.Read(ref _totalDiscovered),
                Volatile.Read(ref _newProducts),
                Volatile.Read(ref _updatedProducts),
                Volatile.Read(ref _selectedForCheck),
                Volatile.Read(ref _checkedProducts),
                Volatile.Read(ref _successfulProducts),
                Volatile.Read(ref _failedProducts),
                _currentStage,
                _currentItem);
        }
    }

    private static int Normalize(int value) => Math.Max(0, value);
}
