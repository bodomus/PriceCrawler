using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;

namespace VarPrice.Infrastructure.Crawler;

public sealed class CategorySeedProductUrlDiscoveryStrategy(
    ICategorySeedProvider seedProvider,
    ICategoryPageLoader pageLoader,
    ICategoryProductLinkExtractor linkExtractor,
    ICategoryPaginationStrategy paginationStrategy,
    IOptions<CrawlerOptions> crawlerOptions,
    ILogger<CategorySeedProductUrlDiscoveryStrategy> logger)
    : IProductUrlDiscoveryStrategy, ICategoryProductUrlDiscoverySource
{
    public ProductUrlDiscoverySourceKind SourceKind => ProductUrlDiscoverySourceKind.CategorySeed;

    public string SourceName => "category-seed";

    public async Task<IReadOnlyCollection<ProductDiscoveryItem>> DiscoverAsync(CancellationToken ct)
    {
        var urls = await DiscoverProductUrlsAsync(ct);
        return urls
            .Select(x => new ProductDiscoveryItem(x.AbsoluteUri, SourceName))
            .ToList();
    }

    public async Task<IReadOnlyCollection<Uri>> DiscoverProductUrlsAsync(CancellationToken ct)
    {
        var seeds = await seedProvider.GetSeedsAsync(ct);
        if (seeds.Count == 0)
        {
            logger.LogWarning("Category seed URL discovery unavailable. Reason=CategorySeedFileEmpty");
            return [];
        }

        var discoveredUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxPagesPerSeed = Math.Max(1, crawlerOptions.Value.MaxCategoryPagesPerSeed);

        foreach (var seed in seeds)
        {
            await DiscoverSeedProductUrlsAsync(seed, discoveredUrls, maxPagesPerSeed, ct);
        }

        logger.LogInformation(
            "Product URL candidates discovered using category seed strategy. SeedCategoryCount={SeedCategoryCount}; ProductUrlCount={ProductUrlCount}",
            seeds.Count,
            discoveredUrls.Count);

        return discoveredUrls.Select(x => new Uri(x)).ToList();
    }

    private async Task DiscoverSeedProductUrlsAsync(
        CategorySeedUrl seed,
        HashSet<string> discoveredUrls,
        int maxPagesPerSeed,
        CancellationToken ct)
    {
        var pageNumber = 1;
        var pageUrl = seed.Url;
        var visitedPageUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            visitedPageUrls.Add(pageUrl.AbsoluteUri);

            logger.LogInformation(
                "Loading category page. Name={Name}; SeedUrl={SeedUrl}; PageUrl={PageUrl}; PageNumber={PageNumber}",
                seed.Name,
                seed.Url,
                pageUrl,
                pageNumber);

            var page = await pageLoader.LoadAsync(seed, pageUrl, ct);
            if (!page.Success || string.IsNullOrWhiteSpace(page.Html))
            {
                LogCategoryPageProcessed(
                    seed,
                    pageUrl,
                    pageNumber,
                    productUrlsFound: 0,
                    newProductUrls: 0,
                    maxPagesPerSeed,
                    stopReason: page.FailureKind ?? CategoryDiscoveryStopReasons.PageUnavailable);
                break;
            }

            var extracted = linkExtractor.ExtractProductUrls(page.Html, pageUrl);
            var newUrls = 0;
            foreach (var productUrl in extracted)
            {
                if (discoveredUrls.Add(productUrl.AbsoluteUri))
                {
                    newUrls++;
                }
            }

            var nextPageUrl = paginationStrategy.GetNextPageUrl(page.Html, pageUrl, visitedPageUrls);
            var stopReason = paginationStrategy.ResolveStopReason(
                newUrls,
                nextPageUrl,
                pageNumber,
                maxPagesPerSeed);
            LogCategoryPageProcessed(seed, pageUrl, pageNumber, extracted.Count, newUrls, maxPagesPerSeed, stopReason);

            if (stopReason is not null)
            {
                break;
            }

            pageUrl = nextPageUrl!;
            pageNumber++;
        }
    }

    private void LogCategoryPageProcessed(
        CategorySeedUrl seed,
        Uri pageUrl,
        int pageNumber,
        int productUrlsFound,
        int newProductUrls,
        int maxPagesPerSeed,
        string? stopReason)
    {
        logger.LogInformation(
            "Category seed page processed. SeedName={SeedName}; SeedUrl={SeedUrl}; PageUrl={PageUrl}; PageNumber={PageNumber}; ProductUrlsFound={ProductUrlsFound}; NewProductUrlsFound={NewProductUrlsFound}; MaxCategoryPagesPerSeed={MaxCategoryPagesPerSeed}; StopReason={StopReason}",
            seed.Name,
            seed.Url,
            pageUrl,
            pageNumber,
            productUrlsFound,
            newProductUrls,
            maxPagesPerSeed,
            stopReason ?? string.Empty);
    }
}

public sealed class CategoryProductUrlDiscoverySource(
    CategorySeedProductUrlDiscoveryStrategy strategy) : ICategoryProductUrlDiscoverySource
{
    public Task<IReadOnlyCollection<Uri>> DiscoverProductUrlsAsync(CancellationToken ct) =>
        strategy.DiscoverProductUrlsAsync(ct);
}
