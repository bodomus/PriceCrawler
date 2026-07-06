namespace PriceCrawler.Infrastructure.Crawler;

public interface ICategoryPageLoader
{
    Task<CategoryPageLoadResult> LoadAsync(CategorySeedUrl seed, Uri pageUrl, CancellationToken ct);
}
