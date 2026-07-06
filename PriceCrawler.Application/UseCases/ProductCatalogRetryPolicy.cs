namespace PriceCrawler.Application.UseCases;

public static class ProductCatalogRetryPolicy
{
    public static TimeSpan ComputeDelay(int consecutiveErrorsBeforeIncrement, int baseDelayMinutes, int maxDelayHours)
    {
        var safeBaseMinutes = Math.Max(1, baseDelayMinutes);
        var safeMaxHours = Math.Max(1, maxDelayHours);
        var safeErrors = Math.Max(0, consecutiveErrorsBeforeIncrement);
        var maxDelay = TimeSpan.FromHours(safeMaxHours);
        var baseDelay = TimeSpan.FromMinutes(safeBaseMinutes);

        if (safeErrors >= 30)
        {
            return maxDelay;
        }

        var multiplier = 1L << safeErrors;
        var delayTicks = baseDelay.Ticks > long.MaxValue / multiplier
            ? long.MaxValue
            : baseDelay.Ticks * multiplier;

        var delay = TimeSpan.FromTicks(delayTicks);
        return delay <= maxDelay ? delay : maxDelay;
    }
}
