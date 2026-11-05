namespace PriceCrawler.Infrastructure.Crawler;

internal static class VarusUrlRules
{
    public static bool IsVarusHttpsUrl(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(uri.Host, "varus.ua", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Derives the stable category key from a Varus category URL: the path without slashes, in lower case
    /// (<c>https://varus.ua/banani</c> gives <c>banani</c>). Filter URLs such as
    /// <c>/moloko~brand_galichina</c> and language-prefixed duplicates are not categories.
    /// </summary>
    /// <param name="uri">Absolute category URL.</param>
    /// <param name="key">The key, when the URL is a category URL.</param>
    /// <returns><see langword="true"/> when <paramref name="uri"/> is a Varus category URL.</returns>
    public static bool TryGetCategoryKey(Uri uri, out string key)
    {
        key = string.Empty;
        if (!uri.IsAbsoluteUri || !IsVarusHttpsUrl(uri))
        {
            return false;
        }

        var path = Uri.UnescapeDataString(uri.AbsolutePath).Trim('/').ToLowerInvariant();
        if (path.Length == 0 || path.Contains('~') || path == "ru" || path.StartsWith("ru/", StringComparison.Ordinal))
        {
            return false;
        }

        key = path;
        return true;
    }
}
