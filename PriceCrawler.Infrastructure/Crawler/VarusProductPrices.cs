using System.Globalization;

namespace PriceCrawler.Infrastructure.Crawler;

/// <summary>
/// Price fields of one product from the inline state of a varus.ua page: the <c>"sqpp":{...}</c> object
/// (<c>price</c> = regular price, <c>special_price</c> = promo price, 0 or null when there is no promo).
/// </summary>
internal sealed record VarusSqpp(
    decimal? RegularPrice,
    decimal? SpecialPrice,
    int? DiscountPercent,
    bool? Available);

/// <summary>Current price, regular (old) price and promo state chosen for a product card.</summary>
internal sealed record VarusResolvedPrice(
    decimal? Price,
    decimal? OldPrice,
    int? DiscountPercent,
    bool PromoFlag,
    bool? InStock,
    bool OldPriceDropped);

/// <summary>
/// Reads the price of the main product from a varus.ua product page (MPC-92).
/// </summary>
/// <remarks>
/// A product page carries ~35 <c>sqpp</c> objects: the main product's one comes first after its
/// <c>"sku":"..."</c>, then the "related products" carousel. The old parser searched for the first
/// numeric <c>special_price</c>/<c>price</c> anywhere in the next 400 KB, so a main product with
/// <c>special_price: null</c> got a carousel product's price (3 538 of 15 217 latest snapshots had
/// <c>old_price &lt; price</c> on 11.11.2026). Now only the top-level fields of the main object are read.
/// </remarks>
internal static class VarusProductPrices
{
    private const int MaxDistanceFromSku = 20_000;

    /// <summary>Finds and parses the main product's <c>sqpp</c> object.</summary>
    /// <param name="html">Raw page HTML.</param>
    /// <param name="sku">Product SKU (JSON-LD <c>sku</c>).</param>
    /// <returns>The parsed object, or <c>null</c> when the SKU or its object is not on the page.</returns>
    public static VarusSqpp? FindMainSqpp(string html, string sku)
    {
        if (string.IsNullOrWhiteSpace(sku)) return null;

        var skuIndex = FindSkuIndex(html, sku);
        if (skuIndex < 0) return null;

        var searchLength = Math.Min(html.Length - skuIndex, MaxDistanceFromSku);
        var keyIndex = html.IndexOf("\"sqpp\"", skuIndex, searchLength, StringComparison.Ordinal);
        if (keyIndex < 0) return null;

        var objectStart = SkipToObjectStart(html, keyIndex + "\"sqpp\"".Length);
        if (objectStart < 0) return null;

        var fields = ReadTopLevelFields(html, objectStart);
        if (fields is null) return null;

        return new VarusSqpp(
            ParseDecimal(fields.GetValueOrDefault("price")),
            ParseDecimal(fields.GetValueOrDefault("special_price")),
            ParseInt(fields.GetValueOrDefault("special_price_discount")),
            ParseBool(fields.GetValueOrDefault("available")));
    }

    /// <summary>
    /// Chooses the card's prices. The JSON-LD <c>offers.price</c> is the price the site shows for the main
    /// product; <c>sqpp</c> gives the regular price and, when it is above zero, the promo price.
    /// </summary>
    /// <param name="jsonLdPrice">JSON-LD <c>offers.price</c>, if any.</param>
    /// <param name="sqpp">Main product's <c>sqpp</c> object, if any.</param>
    /// <returns>Resolved prices; <see cref="VarusResolvedPrice.Price"/> is <c>null</c> when the page has none.</returns>
    public static VarusResolvedPrice Resolve(decimal? jsonLdPrice, VarusSqpp? sqpp)
    {
        var special = Positive(sqpp?.SpecialPrice);
        var regular = Positive(sqpp?.RegularPrice);
        var price = Positive(jsonLdPrice) ?? special ?? regular;

        // A regular price below the current one cannot be right: keep the price, drop the old price.
        var oldPriceDropped = regular.HasValue && price.HasValue && regular.Value < price.Value;
        var oldPrice = oldPriceDropped ? null : regular;

        var discount = sqpp?.DiscountPercent is > 0 ? sqpp.DiscountPercent : CalculateDiscount(oldPrice, price);
        var promo = (discount ?? 0) > 0 || (oldPrice.HasValue && price.HasValue && oldPrice.Value > price.Value);
        return new VarusResolvedPrice(price, oldPrice, discount, promo, sqpp?.Available, oldPriceDropped);
    }

    private static decimal? Positive(decimal? value) => value is > 0m ? value : null;

    private static int? CalculateDiscount(decimal? oldPrice, decimal? price)
    {
        if (oldPrice is not > 0m || price is null || price.Value >= oldPrice.Value) return null;
        var value = (int)Math.Round((oldPrice.Value - price.Value) / oldPrice.Value * 100m, MidpointRounding.AwayFromZero);
        return value > 0 ? value : null;
    }

    private static int FindSkuIndex(string html, string sku)
    {
        foreach (var pattern in (string[])[$"\"sku\":\"{sku}\"", $"\"sku\": \"{sku}\""])
        {
            var index = html.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (index >= 0) return index;
        }

        return -1;
    }

    /// <summary>Skips <c>\s*:\s*</c> after a key and returns the index of <c>{</c>, or -1.</summary>
    private static int SkipToObjectStart(string text, int index)
    {
        index = SkipWhitespace(text, index);
        if (index >= text.Length || text[index] != ':') return -1;
        index = SkipWhitespace(text, index + 1);
        return index < text.Length && text[index] == '{' ? index : -1;
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        return index;
    }

    /// <summary>
    /// Reads the top-level <c>"key": value</c> pairs of the object that starts at <paramref name="start"/>.
    /// Nested objects and arrays are skipped; string values are returned without quotes, other values as raw text.
    /// </summary>
    /// <returns>Fields by key, or <c>null</c> when the object is not closed.</returns>
    internal static Dictionary<string, string>? ReadTopLevelFields(string text, int start)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var index = start + 1;
        while (index < text.Length)
        {
            index = SkipWhitespace(text, index);
            if (index >= text.Length) return null;
            if (text[index] == '}') return fields;
            if (text[index] == ',') { index++; continue; }
            if (text[index] != '"') return null;

            var keyEnd = FindStringEnd(text, index);
            if (keyEnd < 0) return null;
            var key = text[(index + 1)..keyEnd];

            index = SkipWhitespace(text, keyEnd + 1);
            if (index >= text.Length || text[index] != ':') return null;
            index = SkipWhitespace(text, index + 1);

            var valueEnd = FindValueEnd(text, index);
            if (valueEnd < 0) return null;
            var raw = text[index..valueEnd].Trim();
            fields[key] = raw.Length >= 2 && raw[0] == '"' ? raw[1..^1] : raw;
            index = valueEnd;
        }

        return null;
    }

    /// <summary>Returns the index of the closing quote of the string that starts at <paramref name="start"/>.</summary>
    private static int FindStringEnd(string text, int start)
    {
        for (var i = start + 1; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == '"') return i;
        }

        return -1;
    }

    /// <summary>Returns the index just after a value: the next top-level <c>,</c> or <c>}</c>.</summary>
    private static int FindValueEnd(string text, int start)
    {
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '"':
                    i = FindStringEnd(text, i);
                    if (i < 0) return -1;
                    break;
                case '{' or '[':
                    depth++;
                    break;
                case '}' or ']':
                    if (depth == 0) return i;
                    depth--;
                    break;
                case ',' when depth == 0:
                    return i;
            }
        }

        return -1;
    }

    private static decimal? ParseDecimal(string? raw) =>
        decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static int? ParseInt(string? raw) =>
        decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? (int)value : null;

    private static bool? ParseBool(string? raw) =>
        raw is null ? null
        : raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1" ? true
        : raw.Equals("false", StringComparison.OrdinalIgnoreCase) || raw == "0" ? false
        : null;
}
