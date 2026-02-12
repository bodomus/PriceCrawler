using AngleSharp;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace VarPrice.Web.Crawler;

public interface IProductCardExtractor
{
    Task<ProductCard?> ExtractAsync(string url, CancellationToken ct);
}

public sealed class VarusProductCardExtractor(
    IVarusHttpClient http,
    ILogger<VarusProductCardExtractor> log
) : IProductCardExtractor
{
    public async Task<ProductCard?> ExtractAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            log.LogWarning("Cannot parse URL {Url}", url);
            return null;
        }

        var html = await http.GetStringAsync(uri, ct);

        var ctx = BrowsingContext.New(Configuration.Default);
        var doc = await ctx.OpenAsync(req => req.Content(html), ct);

        var name = doc.QuerySelector("h1")?.TextContent?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = doc.Title?.Trim();

        var text = doc.Body?.TextContent ?? "";
        var productId = TryMatchProductId(text);
        if (productId is null)
        {
            log.LogDebug("No product_id found for {Url}", url);
            return null;
        }

        (decimal? packValue, string? packUnit) = PackParser.TryParse(text);

        var (price, oldPrice) = PriceParser.Parse(text);

        var promoFlag = oldPrice.HasValue && oldPrice.Value > price;

        var city = CityParser.TryParseFromUrl(url);

        return new ProductCard(
            ProductId: productId,
            Name: name ?? productId,
            Url: url,
            Price: price,
            OldPrice: oldPrice,
            PromoFlag: promoFlag,
            InStock: null,
            PackValue: packValue,
            PackUnit: packUnit,
            City: city
        );
    }

    private static string? TryMatchProductId(string text)
    {
        string[] markers = ["Артикул:", "Артикул", "SKU:"];
        foreach (var marker in markers)
        {
            var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;

            var tail = text[(idx + marker.Length)..].Trim();
            var digits = new string(tail.TakeWhile(char.IsDigit).ToArray());
            if (!string.IsNullOrWhiteSpace(digits))
                return digits;
        }
        return null;
    }
}

internal static class CityParser
{
    public static string? TryParseFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var seg = uri.Segments.Select(s => s.Trim('/')).FirstOrDefault(s => s.Length > 0);
        return seg;
    }
}

internal static class PackParser
{
    public static (decimal? value, string? unit) TryParse(string text)
    {
        string[] units = ["л", "мл", "кг", "г"];
        foreach (var u in units)
        {
            var idx = text.IndexOf($" {u}", StringComparison.OrdinalIgnoreCase);
            if (idx <= 0) continue;

            var j = idx - 1;
            while (j >= 0 && (char.IsDigit(text[j]) || text[j] == ',' || text[j] == '.' || text[j] == ' ')) j--;
            var num = text[(j + 1)..idx].Trim();

            num = new string(num.Where(c => char.IsDigit(c) || c == ',' || c == '.').ToArray());
            if (decimal.TryParse(num.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                return (v, u);
        }
        return (null, null);
    }
}

internal static class PriceParser
{
    public static (decimal price, decimal? oldPrice) Parse(string text)
    {
        var p = FindFirstMoney(text) ?? 0m;
        var old = FindOldMoney(text);
        return (p, old);
    }

    private static decimal? FindFirstMoney(string text)
    {
        var markers = new[] { "₴", "грн" };
        foreach (var m in markers)
        {
            var idx = text.IndexOf(m, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;

            var j = idx - 1;
            while (j >= 0 && (char.IsDigit(text[j]) || text[j] == ',' || text[j] == '.' || text[j] == ' ')) j--;
            var num = text[(j + 1)..idx].Trim();

            num = new string(num.Where(c => char.IsDigit(c) || c == ',' || c == '.').ToArray());
            if (decimal.TryParse(num.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                return v;
        }
        return null;
    }

    private static decimal? FindOldMoney(string text)
    {
        var marker = "~~";
        var idx = text.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var idx2 = text.IndexOf(marker, idx + 2, StringComparison.Ordinal);
        if (idx2 < 0) return null;
        var num = text[(idx + 2)..idx2].Trim();

        num = new string(num.Where(c => char.IsDigit(c) || c == ',' || c == '.').ToArray());
        if (decimal.TryParse(num.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            return v;

        return null;
    }
}
