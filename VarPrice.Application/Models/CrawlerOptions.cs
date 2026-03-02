namespace VarPrice.Application.Models;

public sealed class CrawlerOptions
{
    /// <summary>Path to the JSON file that contains URL exclusion filters.</summary>
    public string UrlFilterFilePath { get; set; } = string.Empty;
    public string SitemapIndexUrl { get; set; } = string.Empty;
    public string VegetablesUrlContains { get; set; } = string.Empty;
    public int MaxProductsPerRun { get; set; } = 200;
}
