using VarPrice.Application.UseCases;

namespace VarPrice.Web.Tests;

public sealed class QueueRetryPolicyTests
{
    [Fact]
    public void ComputeBackoffDelay_DoublesByAttempt_AndCapsByMax()
    {
        var first = QueueRetryPolicy.ComputeBackoffDelay(failureAttempt: 1, baseDelayMs: 500, maxDelayMs: 5_000,
            jitterMs: 0);
        var second =
            QueueRetryPolicy.ComputeBackoffDelay(failureAttempt: 2, baseDelayMs: 500, maxDelayMs: 5_000, jitterMs: 0);
        var capped =
            QueueRetryPolicy.ComputeBackoffDelay(failureAttempt: 10, baseDelayMs: 500, maxDelayMs: 5_000, jitterMs: 0);

        Assert.Equal(TimeSpan.FromMilliseconds(500), first);
        Assert.Equal(TimeSpan.FromMilliseconds(1_000), second);
        Assert.Equal(TimeSpan.FromMilliseconds(5_000), capped);
    }

    [Fact]
    public void ComputeBackoffDelay_AddsJitter()
    {
        var delay = QueueRetryPolicy.ComputeBackoffDelay(failureAttempt: 2, baseDelayMs: 500, maxDelayMs: 5_000,
            jitterMs: 123);

        Assert.Equal(TimeSpan.FromMilliseconds(1_123), delay);
    }

    [Theory]
    [InlineData(true, 1, 3, QueueFailureAction.Retry)]
    [InlineData(true, 2, 3, QueueFailureAction.Retry)]
    [InlineData(true, 3, 3, QueueFailureAction.Dead)]
    [InlineData(false, 1, 3, QueueFailureAction.Dead)]
    public void DecideFailureAction_ReturnsExpected(bool isTransient, int failureAttempt, int maxAttempts,
        QueueFailureAction expected)
    {
        var action = QueueRetryPolicy.DecideFailureAction(isTransient, failureAttempt, maxAttempts);
        Assert.Equal(expected, action);
    }
}
