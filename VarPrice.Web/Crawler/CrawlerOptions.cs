namespace VarPrice.Web.Crawler;

public sealed class CrawlerOptions
{
    public string SitemapIndexUrl { get; set; } = "";
    public string VegetablesUrlContains { get; set; } = "";
    public int MaxProductsPerRun { get; set; } = 200;
}
