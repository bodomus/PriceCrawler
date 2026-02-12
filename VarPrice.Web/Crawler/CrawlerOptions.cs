namespace VarPrice.Web.Crawler;

public sealed class CrawlerOptions
{
    public Uri SitemapIndexUrl { get; set; } = new("https://varus.ua/sitemap-index.xml");
    public string VegetablesUrlContains { get; set; } = "";
    public int MaxProductsPerRun { get; set; } = 20;
    public int MaxSitemapsToVisit { get; set; } = 200;
    public int MaxUrlsToCollect { get; set; } = 50_000;
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int HttpRetryCount { get; set; } = 4;
}
