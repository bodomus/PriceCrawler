namespace VarPrice.Web.Crawler;

public interface IProductUrlFilter
{
    bool IsProductUrl(Uri url);
}

public sealed class VarusProductUrlFilter : IProductUrlFilter
{
    private static readonly string[] ProductMarkers = ["/product/", "/tovar/", "/item/", "/p/"];
    private static readonly string[] BlockMarkers = ["/blog", "/novosti", "/news", "/akcii"];

    public bool IsProductUrl(Uri url)
    {
        var path = url.AbsolutePath.ToLowerInvariant();

        if (BlockMarkers.Any(path.Contains))
            return false;

        return ProductMarkers.Any(path.Contains);
    }
}
