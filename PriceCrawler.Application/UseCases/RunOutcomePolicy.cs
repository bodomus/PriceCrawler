using PriceCrawler.Domain.Enums;

namespace PriceCrawler.Application.UseCases;

/// <summary>
/// Decides the final status of a queue-based crawler run (price collection and legacy vegetables).
/// A process failure is handled by the callers and is always <see cref="RunStatus.Error"/>.
/// For a run that completed, single dead queue items do not fail the whole run: the run is
/// <see cref="RunStatus.Error"/> only when the share of dead items exceeds the configured threshold.
/// </summary>
public static class RunOutcomePolicy
{
    public const double DefaultDeadThresholdPercent = 1.0d;

    /// <param name="succeeded">Queue items finished successfully.</param>
    /// <param name="retry">Queue items still waiting for a retry when the run finished.</param>
    /// <param name="dead">Queue items that failed permanently.</param>
    /// <param name="deadThresholdPercent">
    /// Maximum share of dead items, in percent of finished items, that still counts as a successful run.
    /// Values are clamped to [0, 100]; 0 restores the strict rule "any dead item fails the run".
    /// </param>
    public static RunStatus Evaluate(int succeeded, int retry, int dead, double deadThresholdPercent)
    {
        if (dead <= 0)
        {
            return RunStatus.Ok;
        }

        var threshold = double.IsNaN(deadThresholdPercent) ? 0d : Math.Clamp(deadThresholdPercent, 0d, 100d);
        return DeadPercent(succeeded, retry, dead) <= threshold ? RunStatus.Ok : RunStatus.Error;
    }

    public static double DeadPercent(int succeeded, int retry, int dead)
    {
        var finished = Math.Max(0, succeeded) + Math.Max(0, retry) + Math.Max(0, dead);
        return finished == 0 ? 0d : Math.Max(0, dead) * 100d / finished;
    }
}
