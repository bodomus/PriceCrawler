using PriceCrawler.Application.UseCases;

namespace PriceCrawler.Web.Tests;

public sealed class ProductCatalogCheckScheduleTests
{
    // Fixed +02:00 zone without DST: results do not depend on the machine running the tests.
    private static readonly TimeZoneInfo PlusTwo =
        TimeZoneInfo.CreateCustomTimeZone("test+02", TimeSpan.FromHours(2), "test+02", "test+02");

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("2026-11-02T10:30:00Z")] // 12:30 local, a late daytime run
    [InlineData("2026-11-02T06:00:00Z")] // 08:00 local, the regular start
    [InlineData("2026-11-02T21:59:00Z")] // 23:59 local
    public void Aligned_DailyInterval_IsDueAtNextLocalMidnight(string checkedAt)
    {
        var next = ProductCatalogCheckSchedule.ComputeNextSuccessfulCheck(
            DateTimeOffset.Parse(checkedAt), 24, alignToDayStart: true, PlusTwo);

        Assert.Equal(DateTimeOffset.Parse("2026-11-02T22:00:00Z"), next);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Aligned_CheckAfterLocalMidnight_BelongsToTheNextLocalDay()
    {
        var next = ProductCatalogCheckSchedule.ComputeNextSuccessfulCheck(
            DateTimeOffset.Parse("2026-11-02T22:30:00Z"), 24, alignToDayStart: true, PlusTwo); // 00:30 local on 03.11

        Assert.Equal(DateTimeOffset.Parse("2026-11-03T22:00:00Z"), next);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(48, "2026-11-03T22:00:00Z")]
    [InlineData(6, "2026-11-02T22:00:00Z")] // under a day: still the next day
    public void Aligned_IntervalIsRoundedToWholeDays(int hours, string expected)
    {
        var next = ProductCatalogCheckSchedule.ComputeNextSuccessfulCheck(
            DateTimeOffset.Parse("2026-11-02T10:30:00Z"), hours, alignToDayStart: true, PlusTwo);

        Assert.Equal(DateTimeOffset.Parse(expected), next);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NotAligned_AddsExactInterval()
    {
        var checkedAt = DateTimeOffset.Parse("2026-11-02T10:30:00Z");

        var next = ProductCatalogCheckSchedule.ComputeNextSuccessfulCheck(checkedAt, 24, alignToDayStart: false, PlusTwo);

        Assert.Equal(checkedAt.AddHours(24), next);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ResolveTimeZone_EmptyMeansLocal()
    {
        Assert.Same(TimeZoneInfo.Local, ProductCatalogCheckSchedule.ResolveTimeZone(""));
        Assert.Same(TimeZoneInfo.Local, ProductCatalogCheckSchedule.ResolveTimeZone(null));
    }
}
