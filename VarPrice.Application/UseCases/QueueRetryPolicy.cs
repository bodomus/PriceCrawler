namespace VarPrice.Application.UseCases;

public enum QueueFailureAction
{
    Retry = 0,
    Dead = 1
}

public static class QueueRetryPolicy
{
    public static QueueFailureAction DecideFailureAction(bool isTransient, int failureAttempt, int maxAttempts)
    {
        if (isTransient && failureAttempt < Math.Max(1, maxAttempts))
        {
            return QueueFailureAction.Retry;
        }

        return QueueFailureAction.Dead;
    }

    public static TimeSpan ComputeBackoffDelay(int failureAttempt, int baseDelayMs, int maxDelayMs, int jitterMs)
    {
        var boundedAttempt = Math.Max(1, failureAttempt);
        var boundedBase = Math.Max(1, baseDelayMs);
        var boundedMax = Math.Max(boundedBase, maxDelayMs);
        var boundedJitter = Math.Max(0, jitterMs);

        var exponent = Math.Max(0, boundedAttempt - 1);
        var delay = boundedBase * Math.Pow(2, exponent);
        delay += boundedJitter;

        return TimeSpan.FromMilliseconds(Math.Min(delay, boundedMax));
    }
}
