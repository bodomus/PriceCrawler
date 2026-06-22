using System.Diagnostics;

using VarPrice.Domain.Constants;
using VarPrice.Domain.Models;

namespace VarPrice.Application.UseCases;

public sealed class CrawlerRunStageRecorder
{
    private static readonly HashSet<string> AllowedStages =
    [
        CrawlerRunStages.Discovery,
        CrawlerRunStages.CatalogUpsert,
        CrawlerRunStages.CatalogDeactivation,
        CrawlerRunStages.CatalogSelection,
        CrawlerRunStages.QueueEnqueue,
        CrawlerRunStages.QueueProcessing,
        CrawlerRunStages.RunFinalization
    ];

    private readonly object _lock = new();
    private readonly List<CrawlerRunStageTiming> _stages = [];
    private readonly HashSet<string> _completedStages = new(StringComparer.Ordinal);

    public void Add(string stage, long durationMs, int? itemCount = null)
    {
        if (!AllowedStages.Contains(stage))
            throw new ArgumentException($"Unsupported crawler run stage '{stage}'.", nameof(stage));
        if (durationMs < 0) throw new ArgumentOutOfRangeException(nameof(durationMs));
        if (itemCount < 0) throw new ArgumentOutOfRangeException(nameof(itemCount));

        lock (_lock)
        {
            if (!_completedStages.Add(stage))
                throw new InvalidOperationException($"Stage '{stage}' has already been completed.");
            _stages.Add(new CrawlerRunStageTiming(stage, durationMs, itemCount));
        }
    }

    public async Task MeasureAsync(string stage, Func<Task> action, int? itemCount = null)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await action();
        }
        finally
        {
            stopwatch.Stop();
            Add(stage, stopwatch.ElapsedMilliseconds, itemCount);
        }
    }

    public IReadOnlyList<CrawlerRunStageTiming> Snapshot()
    {
        lock (_lock)
        {
            return _stages.ToArray();
        }
    }
}
