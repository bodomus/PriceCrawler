namespace VarPrice.Application.Models;

public sealed class CrawlerOptions
{
    /// <summary>Path to the JSON file that contains URL exclusion filters.</summary>
    public string UrlFilterFilePath { get; set; } = string.Empty;

    public string CategorySeedUrlsFilePath { get; set; } = string.Empty;
    public string SitemapIndexUrl { get; set; } = string.Empty;
    public string VegetablesUrlContains { get; set; } = string.Empty;
    public int MaxProductsPerRun { get; set; } = 200;
    public int MaxUrls { get; set; } = 20_000;
    public int MaxCategoryPagesPerSeed { get; set; } = 3;
    public int MaxConcurrency { get; set; } = 4;
    public double RequestsPerSecond { get; set; } = 2.0d;
    public int RequestTimeoutSeconds { get; set; } = 15;
    public int JitterDelayMsMin { get; set; } = 50;
    public int JitterDelayMsMax { get; set; } = 250;
    public int RetryCount { get; set; } = 2;
    public int RetryBaseDelayMs { get; set; } = 500;
    public int BreakerFailureThreshold { get; set; } = 20;
    public int BreakerOpenSeconds { get; set; } = 60;
}
