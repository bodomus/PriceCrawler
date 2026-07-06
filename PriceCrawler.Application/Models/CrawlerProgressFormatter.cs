using System.Globalization;

namespace PriceCrawler.Application.Models;

public static class CrawlerProgressFormatter
{
    private static readonly NumberFormatInfo NumberFormat = new()
    {
        NumberDecimalDigits = 0,
        NumberGroupSeparator = " ",
        NumberGroupSizes = [3]
    };

    public static string FormatNumber(int value) => Math.Max(0, value).ToString("N0", NumberFormat);

    public static string FormatCurrentOverTotal(int current, int total)
    {
        var normalizedCurrent = Math.Max(0, current);
        var normalizedTotal = Math.Max(0, total);

        if (normalizedTotal == 0)
        {
            return normalizedCurrent == 0 ? "-" : FormatNumber(normalizedCurrent);
        }

        return $"{FormatNumber(normalizedCurrent)} / {FormatNumber(normalizedTotal)}";
    }

    public static double CalculatePercent(int current, int total)
    {
        if (current <= 0 || total <= 0)
        {
            return 0d;
        }

        return Math.Min(100d, current * 100d / total);
    }

    public static string FormatPercent(double value) =>
        $"{Math.Clamp(value, 0d, 100d).ToString("0.0", CultureInfo.InvariantCulture)}%";
}
