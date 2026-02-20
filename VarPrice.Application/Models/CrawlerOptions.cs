namespace VarPrice.Application.Models;

public sealed class CrawlerOptions
{
    public string SitemapIndexUrl { get; set; } = string.Empty;
    public string VegetablesUrlContains { get; set; } = string.Empty;
    public int MaxProductsPerRun { get; set; } = 200;
}
