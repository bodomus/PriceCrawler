using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

using AngleSharp;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;

namespace VarPrice.Infrastructure.Crawler;

public sealed class VarusProductCardExtractor(
    IHttpClientFactory httpClientFactory,
    VarusRequestCoordinator requestCoordinator,
    IOptions<CrawlerOptions> options,
    ILogger<VarusProductCardExtractor> log) : IProductCardExtractor
{
    private readonly CrawlerOptions _options = options.Value;

    public async Task<ProductExtractResult> ExtractAsync(string url, CancellationToken ct)
    {
        var maxAttempts = Math.Max(1, _options.RetryCount + 1);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var result = await TryExtractOnceAsync(url, ct);
            var errorCode = result.ErrorCode ?? CrawlerErrorCodes.Unknown;

            if (result.IsTransient)
            {
                requestCoordinator.MarkTemporaryFailure();
            }
            else
            {
                requestCoordinator.ResetTemporaryFailureStreak();
            }

            if (result.IsSuccess)
            {
                return result;
            }

            var canRetry = result.IsTransient && attempt < maxAttempts;
            if (!canRetry)
            {
                return result with
                {
                    Issue = result.Issue is null ? null : result.Issue with { ErrorCode = errorCode }
                };
            }

            var delay = BuildRetryDelay(attempt, errorCode);
            log.LogWarning(
                "Transient extractor error for {Url}. attempt={Attempt}/{MaxAttempts} error_code={ErrorCode} http_status={HttpStatus} retry_delay_ms={RetryDelayMs}",
                url,
                attempt,
                maxAttempts,
                errorCode,
                result.HttpStatus,
                (int)delay.TotalMilliseconds);
            await Task.Delay(delay, ct);
        }

        return ProductExtractResult.Fail(
            CrawlerErrorCodes.Unknown,
            null,
            "Retry loop exited unexpectedly",
            0,
            requestCoordinator.GetApproximateRps(),
            false);
    }

    private async Task<ProductExtractResult> TryExtractOnceAsync(string url, CancellationToken ct)
    {
        await requestCoordinator.AcquireRequestSlotAsync(ct);

        var http = httpClientFactory.CreateClient("varus");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.RequestTimeoutSeconds)));

        var sw = Stopwatch.StartNew();
        int? httpStatus = null;

        try
        {
            using var response =
                await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            httpStatus = (int)response.StatusCode;
            var rps = requestCoordinator.GetApproximateRps();

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                sw.Stop();
                log.LogInformation(
                    "Extractor result {Url} error_code={ErrorCode} http_status={HttpStatus} latency_ms={LatencyMs} current_rps={CurrentRps:F2}",
                    url,
                    CrawlerErrorCodes.NotFound,
                    httpStatus,
                    sw.ElapsedMilliseconds,
                    rps);
                return ProductExtractResult.Fail(
                    CrawlerErrorCodes.NotFound,
                    httpStatus,
                    $"HTTP {httpStatus}",
                    sw.ElapsedMilliseconds,
                    rps,
                    false);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                sw.Stop();
                log.LogWarning(
                    "Extractor result {Url} error_code={ErrorCode} http_status={HttpStatus} latency_ms={LatencyMs} current_rps={CurrentRps:F2}",
                    url,
                    CrawlerErrorCodes.TooManyRequests,
                    httpStatus,
                    sw.ElapsedMilliseconds,
                    rps);
                return ProductExtractResult.Fail(
                    CrawlerErrorCodes.TooManyRequests,
                    httpStatus,
                    $"HTTP {httpStatus}",
                    sw.ElapsedMilliseconds,
                    rps,
                    true);
            }

            if ((int)response.StatusCode >= 500)
            {
                sw.Stop();
                log.LogWarning(
                    "Extractor result {Url} error_code={ErrorCode} http_status={HttpStatus} latency_ms={LatencyMs} current_rps={CurrentRps:F2}",
                    url,
                    CrawlerErrorCodes.Http5xx,
                    httpStatus,
                    sw.ElapsedMilliseconds,
                    rps);
                return ProductExtractResult.Fail(
                    CrawlerErrorCodes.Http5xx,
                    httpStatus,
                    $"HTTP {httpStatus}",
                    sw.ElapsedMilliseconds,
                    rps,
                    true);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                sw.Stop();
                log.LogWarning(
                    "Extractor result {Url} error_code={ErrorCode} http_status={HttpStatus} latency_ms={LatencyMs} current_rps={CurrentRps:F2}",
                    url,
                    CrawlerErrorCodes.Unknown,
                    httpStatus,
                    sw.ElapsedMilliseconds,
                    rps);
                return ProductExtractResult.Fail(
                    CrawlerErrorCodes.Unknown,
                    httpStatus,
                    $"HTTP {httpStatus}",
                    sw.ElapsedMilliseconds,
                    rps,
                    false);
            }

            var html = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var parsed = await TryParseCardAsync(url, html, timeoutCts.Token);
            sw.Stop();

            if (parsed is null)
            {
                log.LogWarning(
                    "Extractor result {Url} error_code={ErrorCode} http_status={HttpStatus} latency_ms={LatencyMs} current_rps={CurrentRps:F2}",
                    url,
                    CrawlerErrorCodes.ParseFailed,
                    httpStatus,
                    sw.ElapsedMilliseconds,
                    rps);
                return ProductExtractResult.Fail(
                    CrawlerErrorCodes.ParseFailed,
                    httpStatus,
                    "Could not parse product card from HTML",
                    sw.ElapsedMilliseconds,
                    rps,
                    false);
            }

            log.LogInformation(
                "Extractor result {Url} status=ok http_status={HttpStatus} latency_ms={LatencyMs} current_rps={CurrentRps:F2}",
                url,
                httpStatus,
                sw.ElapsedMilliseconds,
                rps);
            return ProductExtractResult.Success(parsed, httpStatus, sw.ElapsedMilliseconds, rps);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            sw.Stop();
            var rps = requestCoordinator.GetApproximateRps();
            log.LogWarning(
                "Extractor timeout for {Url} latency_ms={LatencyMs} current_rps={CurrentRps:F2}",
                url,
                sw.ElapsedMilliseconds,
                rps);
            return ProductExtractResult.Fail(
                CrawlerErrorCodes.Timeout,
                httpStatus,
                $"Request timed out after {_options.RequestTimeoutSeconds}s",
                sw.ElapsedMilliseconds,
                rps,
                true);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            var rps = requestCoordinator.GetApproximateRps();
            log.LogWarning(
                "Extractor timeout (task canceled) for {Url} latency_ms={LatencyMs} current_rps={CurrentRps:F2}",
                url,
                sw.ElapsedMilliseconds,
                rps);
            return ProductExtractResult.Fail(
                CrawlerErrorCodes.Timeout,
                httpStatus,
                $"Request timed out after {_options.RequestTimeoutSeconds}s",
                sw.ElapsedMilliseconds,
                rps,
                true);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            sw.Stop();
            var rps = requestCoordinator.GetApproximateRps();
            return ProductExtractResult.Fail(
                CrawlerErrorCodes.TooManyRequests,
                (int?)HttpStatusCode.TooManyRequests,
                ex.Message,
                sw.ElapsedMilliseconds,
                rps,
                true);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is not null && (int)ex.StatusCode >= 500)
        {
            sw.Stop();
            var rps = requestCoordinator.GetApproximateRps();
            return ProductExtractResult.Fail(
                CrawlerErrorCodes.Http5xx,
                (int?)ex.StatusCode,
                ex.Message,
                sw.ElapsedMilliseconds,
                rps,
                true);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var rps = requestCoordinator.GetApproximateRps();

            if (!ct.IsCancellationRequested && IsTimeoutLike(ex))
            {
                log.LogWarning(
                    ex,
                    "Extractor timeout-like error for {Url} latency_ms={LatencyMs} current_rps={CurrentRps:F2}",
                    url,
                    sw.ElapsedMilliseconds,
                    rps);
                return ProductExtractResult.Fail(
                    CrawlerErrorCodes.Timeout,
                    httpStatus,
                    $"Request timed out after {_options.RequestTimeoutSeconds}s",
                    sw.ElapsedMilliseconds,
                    rps,
                    true);
            }

            log.LogWarning(ex, "Extractor unknown error for {Url}", url);
            return ProductExtractResult.Fail(
                CrawlerErrorCodes.Unknown,
                httpStatus,
                ex.Message,
                sw.ElapsedMilliseconds,
                rps,
                false);
        }
    }

    private async Task<ProductCard?> TryParseCardAsync(string url, string html, CancellationToken ct)
    {
        var ctx = BrowsingContext.New(Configuration.Default);
        var doc = await ctx.OpenAsync(req => req.Content(html), ct);

        var ld = JsonLdProductParser.TryParse(html);

        // Prefer JSON-LD values (stable, no DOM rendering needed).
        var name = (ld?.Name?.Trim()).NullIfEmpty()
                   ?? doc.QuerySelector("h1")?.TextContent?.Trim()
                   ?? doc.Title?.Trim();

        var text = doc.Body?.TextContent ?? string.Empty;
        string? externalId = (ld?.Sku?.Trim()).NullIfEmpty() ?? TryMatchProductId(text);

        if (externalId is null)
        {
            log.LogDebug("No external_id found for {Url}", url);
        }
        else
        {
            log.LogDebug("external_id found for {Url} external_id={ExternalId}", url, externalId);
        }

        var (packValue, packUnit) = PackParser.TryParse(text);

        // Price strategy:
        // 1) Current price from JSON-LD offers.price
        // 2) Old/regular/special from inline JSON fragment near SKU (sqpp.price / sqpp.special_price)
        // 3) Fallback to legacy text scraping (last resort)
        decimal? finalPrice = ld?.Price;
        decimal? regularPrice = null;
        int? discountPercent = null;
        bool? inStock = null;

        var sqpp = SkuInlineJsonFallback.TryExtractSqpp(html, externalId ?? string.Empty);
        if (sqpp is not null)
        {
            // On Varus: sqpp.special_price is promo price, sqpp.price is regular price.
            if (sqpp.SpecialPrice is not null)
                finalPrice = sqpp.SpecialPrice.Value;

            if (sqpp.RegularPrice is not null)
                regularPrice = sqpp.RegularPrice.Value;

            discountPercent = sqpp.DiscountPercent;
            inStock = sqpp.Available;

            log.LogDebug("SQPP found for {Sku}: regular={Regular} special={Special} available={Available}",
                externalId, sqpp.RegularPrice, sqpp.SpecialPrice, sqpp.Available);
        }
        else
        {
            log.LogDebug("SQPP not found for {ExternalId}", externalId);
        }

        if (!finalPrice.HasValue || finalPrice.Value <= 0m)
        {
            var legacy = PriceParser.Parse(text);
            if (legacy.price > 0m)
            {
                finalPrice = legacy.price;
            }

            regularPrice ??= legacy.oldPrice;
        }

        if ((!finalPrice.HasValue || finalPrice.Value <= 0m) && regularPrice.HasValue)
        {
            finalPrice = regularPrice;
        }

        if ((!finalPrice.HasValue || finalPrice.Value <= 0m) && !regularPrice.HasValue && !inStock.HasValue)
        {
            log.LogDebug("No valid price found for {Url}", url);
            return null;
        }

        discountPercent ??= CalculateDiscountPercent(regularPrice, finalPrice);
        var promoFlag = (discountPercent.HasValue && discountPercent.Value > 0)
                        || (regularPrice.HasValue && finalPrice.HasValue && regularPrice.Value > finalPrice.Value);

        return new ProductCard(
            externalId,
            name ?? externalId ?? url,
            url,
            TryBuildSlug(url),
            finalPrice,
            regularPrice,
            promoFlag,
            inStock ?? false,
            packValue,
            packUnit);
    }

    private TimeSpan BuildRetryDelay(int attempt, string errorCode)
    {
        var exponent = Math.Max(0, attempt - 1);
        var baseDelayMs = Math.Max(1, _options.RetryBaseDelayMs);
        var backoffMultiplier = Math.Pow(2, exponent);
        var delayMs = baseDelayMs * backoffMultiplier;

        if (string.Equals(errorCode, CrawlerErrorCodes.TooManyRequests, StringComparison.OrdinalIgnoreCase))
        {
            delayMs *= 2;
        }

        if (_options.JitterDelayMsMax > 0)
        {
            var jitterMin = Math.Max(0, _options.JitterDelayMsMin);
            var jitterMax = Math.Max(jitterMin, _options.JitterDelayMsMax);
            delayMs += Random.Shared.Next(jitterMin, jitterMax + 1);
        }

        return TimeSpan.FromMilliseconds(Math.Min(delayMs, 30_000d));
    }

    private static bool IsTimeoutLike(Exception ex)
        => ex is TimeoutException or OperationCanceledException or TaskCanceledException
           || (ex.InnerException is not null && IsTimeoutLike(ex.InnerException));

    private static string? TryMatchProductId(string text)
    {
        string[] markers = ["Артикул:", "Артикул", "SKU:"];
        foreach (var marker in markers)
        {
            var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            var tail = text[(idx + marker.Length)..].Trim();
            var digits = new string(tail.TakeWhile(char.IsDigit).ToArray());
            if (!string.IsNullOrWhiteSpace(digits)) return digits;
        }

        return null;
    }

    private static int? CalculateDiscountPercent(decimal? regularPrice, decimal? finalPrice)
    {
        if (!regularPrice.HasValue || !finalPrice.HasValue || regularPrice.Value <= 0m ||
            finalPrice.Value >= regularPrice.Value)
        {
            return null;
        }

        var value = ((regularPrice.Value - finalPrice.Value) / regularPrice.Value) * 100m;
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static string? TryBuildSlug(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Segments
            .Select(segment => segment.Trim('/'))
            .LastOrDefault(segment => !string.IsNullOrWhiteSpace(segment));
    }
}

public static class PackParser
{
    public static (decimal? value, string? unit) TryParse(string text)
    {
        var quantityTextMatch = Regex.Match(
            text,
            "\"quantityText\"\\s*:\\s*\"(?<unit>[^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (quantityTextMatch.Success)
        {
            var quantityUnit = quantityTextMatch.Groups["unit"].Value.Trim();
            var valueMatch = Regex.Match(
                quantityUnit,
                "(?<value>\\d+(?:[\\.,]\\d+)?)\\s*(?:л|мл|кг|г)\\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (valueMatch.Success &&
                decimal.TryParse(
                    valueMatch.Groups["value"].Value.Replace(',', '.'),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var quantityValue))
            {
                return (quantityValue, quantityUnit);
            }

            return (null, quantityUnit);
        }

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

public static class PriceParser
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

file static class StringExt
{
    public static string? NullIfEmpty(this string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;
}

file sealed record JsonLdProduct(string? Name, string? Sku, decimal? Price, string? Currency, string? Availability);

file static class JsonLdProductParser
{
    public static JsonLdProduct? TryParse(string html)
    {
        foreach (var scriptJson in ExtractLdJsonScripts(html))
        {
            var product = TryParseProductFromJson(scriptJson);
            if (product is not null) return product;
        }

        return null;
    }

    private static IEnumerable<string> ExtractLdJsonScripts(string html)
    {
        // Matches: <script ... type="application/ld+json" ...> ... </script>
        var rx = new Regex(
            "<script[^>]*type=\"application/ld\\+json\"[^>]*>(?<json>[\\s\\S]*?)</script>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        foreach (Match m in rx.Matches(html))
        {
            var json = m.Groups["json"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(json))
                yield return json;
        }
    }

    private static JsonLdProduct? TryParseProductFromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var p = TryParseProductObject(el);
                    if (p is not null) return p;
                }

                return null;
            }

            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                return TryParseProductObject(doc.RootElement);
        }
        catch
        {
            // ignore invalid blocks
        }

        return null;
    }

    private static JsonLdProduct? TryParseProductObject(JsonElement obj)
    {
        if (!IsTypeProduct(obj)) return null;

        var name = GetString(obj, "name");
        var sku = GetString(obj, "sku");

        decimal? price = null;
        string? currency = null;
        string? availability = null;

        if (obj.TryGetProperty("offers", out var offers))
        {
            if (offers.ValueKind == JsonValueKind.Object)
            {
                (price, currency, availability) = ParseOffer(offers);
            }
            else if (offers.ValueKind == JsonValueKind.Array)
            {
                foreach (var o in offers.EnumerateArray())
                {
                    if (o.ValueKind != JsonValueKind.Object) continue;
                    (price, currency, availability) = ParseOffer(o);
                    if (price is not null) break;
                }
            }
        }

        return new JsonLdProduct(name, sku, price, currency, availability);
    }

    private static (decimal? price, string? currency, string? availability) ParseOffer(JsonElement offer)
    {
        decimal? price = null;

        if (offer.TryGetProperty("price", out var p))
        {
            if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var dv))
                price = dv;
            else if (p.ValueKind == JsonValueKind.String &&
                     decimal.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var ds))
                price = ds;
        }

        var currency = GetString(offer, "priceCurrency");
        var availability = GetString(offer, "availability");
        return (price, currency, availability);
    }

    private static bool IsTypeProduct(JsonElement obj)
    {
        if (!obj.TryGetProperty("@type", out var t)) return false;

        if (t.ValueKind == JsonValueKind.String)
            return string.Equals(t.GetString(), "Product", StringComparison.OrdinalIgnoreCase);

        if (t.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in t.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String &&
                    string.Equals(el.GetString(), "Product", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static string? GetString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

file sealed record SqppInfo(
    decimal? RegularPrice,
    decimal? SpecialPrice,
    int? DiscountPercent,
    DateOnly? SpecialFromDate,
    bool? Available);

file static class SkuInlineJsonFallback
{
    // Extracts "sqpp" fields from inline JSON fragment near a given SKU.
    public static SqppInfo? TryExtractSqpp(string html, string sku)
    {
        if (string.IsNullOrWhiteSpace(sku)) return null;

        var idx = FindSkuIndex(html, sku);
        if (idx < 0) return null;

        const int before = 200_000;
        const int window = 400_000;

        var start = Math.Max(0, idx - before);
        var len = Math.Min(html.Length - start, window);
        var slice = html.Substring(start, len);

        var sqppIdx = slice.IndexOf("\"sqpp\"", StringComparison.OrdinalIgnoreCase);
        if (sqppIdx < 0) return null;

        var tail = slice.Substring(sqppIdx);

        var special = TryMatchDecimal(tail, "special_price");
        var regular = TryMatchDecimal(tail, "price");
        var discount = TryMatchInt(tail, "special_price_discount");
        var available = TryMatchBool(tail, "available");
        var fromDate = TryMatchDate(tail, "special_price_from_date");

        if (special is null && regular is null && discount is null && available is null && fromDate is null)
            return null;

        return new SqppInfo(regular, special, discount, fromDate, available);
    }

    private static int FindSkuIndex(string html, string sku)
    {
        var a = html.IndexOf($"\"sku\":\"{sku}\"", StringComparison.OrdinalIgnoreCase);
        if (a >= 0) return a;

        var b = html.IndexOf($"\"sku\": \"{sku}\"", StringComparison.OrdinalIgnoreCase);
        if (b >= 0) return b;

        return html.IndexOf(sku, StringComparison.OrdinalIgnoreCase);
    }

    private static decimal? TryMatchDecimal(string text, string key)
    {
        var rx = new Regex($"\\\"{Regex.Escape(key)}\\\"\\s*:\\s*(?<v>[0-9]+(?:\\.[0-9]+)?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var m = rx.Match(text);
        if (!m.Success) return null;

        return decimal.TryParse(m.Groups["v"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }

    private static int? TryMatchInt(string text, string key)
    {
        var rx = new Regex($"\\\"{Regex.Escape(key)}\\\"\\s*:\\s*(?<v>[0-9]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var m = rx.Match(text);
        if (!m.Success) return null;

        return int.TryParse(m.Groups["v"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }

    private static bool? TryMatchBool(string text, string key)
    {
        var rx = new Regex($"\\\"{Regex.Escape(key)}\\\"\\s*:\\s*(?<v>true|false)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var m = rx.Match(text);
        if (!m.Success) return null;

        return string.Equals(m.Groups["v"].Value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static DateOnly? TryMatchDate(string text, string key)
    {
        var rx = new Regex($"\\\"{Regex.Escape(key)}\\\"\\s*:\\s*\\\"(?<v>[0-9]{{4}}-[0-9]{{2}}-[0-9]{{2}})\\\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var m = rx.Match(text);
        if (!m.Success) return null;

        return DateOnly.TryParseExact(m.Groups["v"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d)
            ? d
            : null;
    }
}
