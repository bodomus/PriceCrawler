namespace VarPrice.Infrastructure.Crawler;

internal static class VarusUrlRules
{
    public static bool IsVarusHttpsUrl(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(uri.Host, "varus.ua", StringComparison.OrdinalIgnoreCase);
}
