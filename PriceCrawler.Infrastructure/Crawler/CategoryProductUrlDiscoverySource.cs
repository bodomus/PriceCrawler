using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PriceCrawler.Application.Abstractions;
using PriceCrawler.Application.Models;
using PriceCrawler.Domain.Categories;

namespace PriceCrawler.Infrastructure.Crawler;

/// <summary>
/// Discovers product URLs by crawling category listings. Categories come from the store's category
/// tree (its leaves) or, as a fallback, from the seed file. Records which category listed which product.
/// </summary>
public sealed class CategorySeedProductUrlDiscoveryStrategy(
    ICategorySeedProvider seedProvider,
    ICategoryTreeSource categoryTreeSource,
    ICategoryPageLoader pageLoader,
    ICategoryProductLinkExtractor linkExtractor,
    ICategoryPaginationStrategy paginationStrategy,
    IOptions<CrawlerOptions> crawlerOptions,
    ICrawlerProgressReporter progressReporter,
    ILogger<CategorySeedProductUrlDiscoveryStrategy> logger)
    : IProductUrlDiscoveryStrategy, ICategoryProductUrlDiscoverySource, ICategoryAwareDiscoveryStrategy
{
    public ProductUrlDiscoverySourceKind SourceKind => ProductUrlDiscoverySourceKind.CategorySeed;

    public string SourceName => "category-seed";

    /// <inheritdoc />
    public CategoryDiscovery? LastCategoryDiscovery { get; private set; }

    public async Task<IReadOnlyCollection<ProductDiscoveryItem>> DiscoverAsync(CancellationToken ct)
    {
        var urls = await DiscoverProductUrlsAsync(ct);
        return urls
            .Select(x => new ProductDiscoveryItem(x.AbsoluteUri, SourceName))
            .ToList();
    }

    public async Task<IReadOnlyCollection<Uri>> DiscoverProductUrlsAsync(CancellationToken ct)
    {
        var (seeds, tree) = await ResolveSeedsAsync(ct);
        LastCategoryDiscovery = new CategoryDiscovery(tree, []);
        if (seeds.Count == 0)
        {
            logger.LogWarning("Category seed URL discovery unavailable. Reason=CategorySeedFileEmpty");
            return [];
        }

        var discoveredUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var listings = new List<CategoryProductListing>(seeds.Count);
        var maxPagesPerSeed = Math.Max(1, crawlerOptions.Value.MaxCategoryPagesPerSeed);

        // Leaf categories barely overlap, so each one is read to its last page. Seed-file categories
        // nest (parents come first), so there a page without new products ends the category early.
        var stopWhenNoNewProducts = tree is null;

        for (var seedIndex = 0; seedIndex < seeds.Count; seedIndex++)
        {
            var listing = await DiscoverSeedProductUrlsAsync(
                seeds[seedIndex],
                discoveredUrls,
                maxPagesPerSeed,
                stopWhenNoNewProducts,
                seedIndex + 1,
                seeds.Count,
                ct);
            if (listing is not null)
            {
                listings.Add(listing);
            }
        }

        LastCategoryDiscovery = new CategoryDiscovery(tree, listings);
        logger.LogInformation(
            "Product URL candidates discovered using category seed strategy. SeedSource={SeedSource}; SeedCategoryCount={SeedCategoryCount}; ProductUrlCount={ProductUrlCount}; CompleteCategoryCount={CompleteCategoryCount}",
            tree is null ? CategorySeedSources.File : CategorySeedSources.Tree,
            seeds.Count,
            discoveredUrls.Count,
            listings.Count(listing => listing.IsComplete));

        return discoveredUrls.Select(x => new Uri(x)).ToList();
    }

    private async Task<(IReadOnlyList<CategorySeedUrl> Seeds, CategoryTreeSnapshot? Tree)> ResolveSeedsAsync(
        CancellationToken ct)
    {
        if (string.Equals(crawlerOptions.Value.CategorySeedSource, CategorySeedSources.Tree,
                StringComparison.OrdinalIgnoreCase))
        {
            var tree = await categoryTreeSource.GetTreeAsync(ct);
            if (tree is not null && tree.Leaves.Count > 0)
            {
                var seeds = tree.Leaves
                    .Select(leaf => new CategorySeedUrl(leaf.Name, new Uri(leaf.Url)))
                    .ToList();
                logger.LogInformation(
                    "Category seeds taken from the category tree. Categories={Categories}; Leaves={Leaves}",
                    tree.Nodes.Count,
                    seeds.Count);
                return (seeds, tree);
            }

            logger.LogWarning(
                "Category tree unavailable; falling back to the category seed file. ConfigurationKey=Crawler:CategorySeedUrlsFilePath");
        }

        return (await seedProvider.GetSeedsAsync(ct), null);
    }

    /// <summary>Reads the pages of one category and records the products it lists.</summary>
    /// <returns>The category listing, or <see langword="null"/> when the seed URL is not a category URL.</returns>
    private async Task<CategoryProductListing?> DiscoverSeedProductUrlsAsync(
        CategorySeedUrl seed,
        HashSet<string> discoveredUrls,
        int maxPagesPerSeed,
        bool stopWhenNoNewProducts,
        int processedSeeds,
        int totalSeeds,
        CancellationToken ct)
    {
        var pageNumber = 1;
        var pageUrl = seed.Url;
        var visitedPageUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var listedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? finalStopReason;

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
            ReportDiscoveryProgress(processedSeeds, totalSeeds, discoveredUrls.Count, seed, pageNumber);

            var page = await pageLoader.LoadAsync(seed, pageUrl, ct);
            if (!page.Success || string.IsNullOrWhiteSpace(page.Html))
            {
                ReportDiscoveryProgress(processedSeeds, totalSeeds, discoveredUrls.Count, seed, pageNumber);
                LogCategoryPageProcessed(
                    seed,
                    pageUrl,
                    pageNumber,
                    productUrlsFound: 0,
                    newProductUrls: 0,
                    maxPagesPerSeed,
                    stopReason: page.FailureKind ?? CategoryDiscoveryStopReasons.PageUnavailable);
                finalStopReason = CategoryDiscoveryStopReasons.PageUnavailable;
                break;
            }

            var extracted = linkExtractor.ExtractProductUrls(page.Html, pageUrl);
            var newUrls = 0;
            foreach (var productUrl in extracted)
            {
                listedUrls.Add(productUrl.AbsoluteUri);
                if (discoveredUrls.Add(productUrl.AbsoluteUri))
                {
                    newUrls++;
                }
            }

            var nextPageUrl = paginationStrategy.GetNextPageUrl(page.Html, pageUrl, visitedPageUrls);
            var stopReason = paginationStrategy.ResolveStopReason(
                stopWhenNoNewProducts ? newUrls : extracted.Count,
                nextPageUrl,
                pageNumber,
                maxPagesPerSeed);
            ReportDiscoveryProgress(processedSeeds, totalSeeds, discoveredUrls.Count, seed, pageNumber);
            LogCategoryPageProcessed(seed, pageUrl, pageNumber, extracted.Count, newUrls, maxPagesPerSeed, stopReason);

            if (stopReason is not null)
            {
                finalStopReason = stopReason;
                break;
            }

            pageUrl = nextPageUrl!;
            pageNumber++;
        }

        if (!VarusUrlRules.TryGetCategoryKey(seed.Url, out var categoryKey))
        {
            return null;
        }

        // Only a category read to its last page proves that an unlisted product left it.
        var isComplete = finalStopReason == CategoryDiscoveryStopReasons.NoNextPage;
        return new CategoryProductListing(categoryKey, listedUrls.ToList(), isComplete);
    }

    private void ReportDiscoveryProgress(
        int processedSeeds,
        int totalSeeds,
        int discoveredProductUrls,
        CategorySeedUrl seed,
        int pageNumber)
    {
        progressReporter.SetDiscoveryProgress(
            processedSeeds,
            totalSeeds,
            discoveredProductUrls,
            seed.Name,
            seed.Url.AbsoluteUri,
            pageNumber);
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
