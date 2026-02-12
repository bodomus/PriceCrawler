namespace VarPrice.Web.Crawler;

public interface IProductUrlFilter
{
    bool IsProductUrl(Uri url);
    bool IsTestUrl(Uri url);
}

public sealed class VarusProductUrlFilter : IProductUrlFilter
{
    private static readonly string[] ProductMarkers = ["/product/", "/tovar/", "/item/", "/p/", "/shampun"];
    private static readonly string[] BlockMarkers = ["/blog", "/novosti", "/news", "/akcii", "/test1"];

    public bool IsProductUrl(Uri url)
    {
        var path = url.AbsolutePath.ToLowerInvariant();

        if (BlockMarkers.Any(path.Contains))
            return false;
        // return true;
        return ProductMarkers.Any(path.Contains);
        return false;
    }

    public bool IsTestUrl(Uri url)
    {
        var path = url.AbsolutePath.ToLowerInvariant();
        if (BlockMarkers.Any(path.Contains))
            return true;
        return false;
    }
}
