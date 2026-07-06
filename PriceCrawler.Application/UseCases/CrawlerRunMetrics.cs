using PriceCrawler.Domain.Models;

namespace PriceCrawler.Application.UseCases;

public sealed class CrawlerRunMetrics
{
    private int _discovered;
    private int _accepted;
    private int _inserted;
    private int _updated;
    private int _reactivated;
    private int _deactivated;
    private int _selected;
    private int _enqueued;
    private int _succeeded;
    private int _retry;
    private int _dead;
    private int _failed;
    private int _productsCreated;
    private int _productsUpdated;
    private int _snapshotsCreated;
    private int _errorsCreated;

    public void SetCatalog(int discovered, int accepted, int inserted, int updated, int reactivated, int deactivated)
    {
        _discovered = NonNegative(discovered);
        _accepted = NonNegative(accepted);
        _inserted = NonNegative(inserted);
        _updated = NonNegative(updated);
        _reactivated = NonNegative(reactivated);
        _deactivated = NonNegative(deactivated);
    }

    public void SetSelection(int selected, int enqueued)
    {
        _selected = NonNegative(selected);
        _enqueued = NonNegative(enqueued);
    }

    public void SetQueue(int succeeded, int retry, int dead)
    {
        _succeeded = NonNegative(succeeded);
        _retry = NonNegative(retry);
        _dead = NonNegative(dead);
        _failed = checked(_retry + _dead);
    }

    public void RecordObservation(bool productCreated, bool productUpdated, bool snapshotCreated, bool errorCreated)
    {
        if (productCreated) Interlocked.Increment(ref _productsCreated);
        if (productUpdated) Interlocked.Increment(ref _productsUpdated);
        if (snapshotCreated) Interlocked.Increment(ref _snapshotsCreated);
        if (errorCreated) Interlocked.Increment(ref _errorsCreated);
    }

    public void IncrementError() => Interlocked.Increment(ref _errorsCreated);

    public CrawlerRunStatistics Snapshot() => new(
        Volatile.Read(ref _discovered), Volatile.Read(ref _accepted), Volatile.Read(ref _inserted),
        Volatile.Read(ref _updated), Volatile.Read(ref _reactivated), Volatile.Read(ref _deactivated),
        Volatile.Read(ref _selected), Volatile.Read(ref _enqueued), Volatile.Read(ref _succeeded),
        Volatile.Read(ref _retry), Volatile.Read(ref _dead), Volatile.Read(ref _failed),
        Volatile.Read(ref _productsCreated), Volatile.Read(ref _productsUpdated),
        Volatile.Read(ref _snapshotsCreated), Volatile.Read(ref _errorsCreated));

    private static int NonNegative(int value) =>
        value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
}
