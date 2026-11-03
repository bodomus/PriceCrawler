namespace PriceCrawler.Application.UseCases;

/// <summary>
/// When a successfully checked catalog item becomes due again (MPC-94).
/// </summary>
public static class ProductCatalogCheckSchedule
{
    /// <summary>
    /// Without alignment: <paramref name="checkedAtUtc"/> + interval.
    /// With alignment: the start of the local day that is <c>round(interval / 24)</c> days (at least 1) after the
    /// local date of the check. A daily collection then finds the whole catalog due on the next day, whatever time
    /// it starts (late after a power cut) and however long the previous run took.
    /// </summary>
    public static DateTimeOffset ComputeNextSuccessfulCheck(
        DateTimeOffset checkedAtUtc,
        int intervalHours,
        bool alignToDayStart,
        TimeZoneInfo timeZone)
    {
        var hours = Math.Max(1, intervalHours);
        if (!alignToDayStart)
        {
            return checkedAtUtc.AddHours(hours);
        }

        var days = Math.Max(1, (int)Math.Round(hours / 24.0, MidpointRounding.AwayFromZero));
        var localDate = TimeZoneInfo.ConvertTime(checkedAtUtc, timeZone).Date;
        var nextLocalStart = DateTime.SpecifyKind(localDate.AddDays(days), DateTimeKind.Unspecified);
        return new DateTimeOffset(nextLocalStart, timeZone.GetUtcOffset(nextLocalStart)).ToUniversalTime();
    }

    /// <summary>Configured time zone id, or the computer's local time zone when empty.</summary>
    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId) =>
        string.IsNullOrWhiteSpace(timeZoneId) ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
}
