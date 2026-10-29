using PriceCrawler.Application.UseCases;
using PriceCrawler.Domain.Enums;

namespace PriceCrawler.Web.Tests;

public sealed class RunOutcomePolicyTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(10, 0, 0, 1.0, RunStatus.Ok)]
    [InlineData(0, 0, 0, 1.0, RunStatus.Ok)]
    [InlineData(10258, 0, 1, 1.0, RunStatus.Ok)]
    [InlineData(99, 0, 1, 1.0, RunStatus.Ok)]
    [InlineData(98, 0, 2, 1.0, RunStatus.Error)]
    [InlineData(0, 0, 1, 1.0, RunStatus.Error)]
    [InlineData(10258, 0, 1, 0.0, RunStatus.Error)]
    [InlineData(90, 5, 5, 10.0, RunStatus.Ok)]
    [InlineData(1, 0, 1, 100.0, RunStatus.Ok)]
    public void Evaluate_UsesDeadShareThreshold(int succeeded, int retry, int dead, double threshold, RunStatus expected)
    {
        Assert.Equal(expected, RunOutcomePolicy.Evaluate(succeeded, retry, dead, threshold));
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    public void Evaluate_NegativeOrNaNThreshold_FallsBackToStrictRule(double threshold)
    {
        Assert.Equal(RunStatus.Error, RunOutcomePolicy.Evaluate(10258, 0, 1, threshold));
        Assert.Equal(RunStatus.Ok, RunOutcomePolicy.Evaluate(10258, 0, 0, threshold));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DeadPercent_IsShareOfFinishedItems()
    {
        Assert.Equal(0d, RunOutcomePolicy.DeadPercent(0, 0, 0));
        Assert.Equal(50d, RunOutcomePolicy.DeadPercent(1, 0, 1));
        Assert.Equal(10d, RunOutcomePolicy.DeadPercent(85, 5, 10));
    }
}
