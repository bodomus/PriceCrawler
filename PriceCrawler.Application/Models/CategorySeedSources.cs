namespace PriceCrawler.Application.Models;

/// <summary>Allowed values of <see cref="CrawlerOptions.CategorySeedSource"/>.</summary>
public static class CategorySeedSources
{
    /// <summary>Crawl the leaf categories of the category tree read from the store site.</summary>
    public const string Tree = "Tree";

    /// <summary>Crawl the categories listed in the category seed file.</summary>
    public const string File = "File";
}
