using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PriceCrawler.Application.Abstractions;
using PriceCrawler.Application.Models;
using PriceCrawler.Domain.Categories;

namespace PriceCrawler.Infrastructure.Crawler;

/// <summary>Reads the Varus category tree from the category menu that every varus.ua page carries.</summary>
public sealed class VarusCategoryTreeSource(
    ICategoryPageLoader pageLoader,
    IVarusCategoryMenuParser menuParser,
    IOptions<CrawlerOptions> crawlerOptions,
    ILogger<VarusCategoryTreeSource> logger) : ICategoryTreeSource
{
    /// <summary>Store key used for Varus categories.</summary>
    public const string Source = "varus";

    /// <inheritdoc />
    public async Task<CategoryTreeSnapshot?> GetTreeAsync(CancellationToken ct)
    {
        var options = crawlerOptions.Value;
        if (!Uri.TryCreate(options.CategoryTreeUrl, UriKind.Absolute, out var pageUrl) ||
            !VarusUrlRules.IsVarusHttpsUrl(pageUrl))
        {
            logger.LogWarning(
                "Category tree unavailable. Reason=InvalidCategoryTreeUrl; ConfigurationKey=Crawler:CategoryTreeUrl");
            return null;
        }

        var page = await pageLoader.LoadAsync(new CategorySeedUrl("Category menu", pageUrl), pageUrl, ct);
        if (!page.Success || string.IsNullOrWhiteSpace(page.Html))
        {
            logger.LogWarning(
                "Category tree unavailable. Reason={Reason}; PageUrl={PageUrl}",
                page.FailureKind ?? CategoryDiscoveryStopReasons.PageUnavailable,
                pageUrl);
            return null;
        }

        CategoryTreeSnapshot tree;
        try
        {
            tree = CategoryTreeSnapshot.Create(Source, menuParser.Parse(page.Html, pageUrl));
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Category tree unavailable. Reason=InvalidCategoryTree; PageUrl={PageUrl}", pageUrl);
            return null;
        }

        var minimum = Math.Max(1, options.CategoryTreeMinimumNodes);
        if (tree.Nodes.Count < minimum)
        {
            logger.LogWarning(
                "Category tree unavailable. Reason=CategoryTreeTooSmall; Categories={Categories}; Minimum={Minimum}; PageUrl={PageUrl}",
                tree.Nodes.Count,
                minimum,
                pageUrl);
            return null;
        }

        logger.LogInformation(
            "Category tree read. Categories={Categories}; Leaves={Leaves}; Duplicates={Duplicates}; PageUrl={PageUrl}",
            tree.Nodes.Count,
            tree.Leaves.Count,
            tree.DuplicateCount,
            pageUrl);
        return tree;
    }
}

/// <summary>Extracts categories from the category menu of a varus.ua page.</summary>
public interface IVarusCategoryMenuParser
{
    /// <summary>Parses the menu into categories in site order, parents before children.</summary>
    /// <param name="html">Page HTML.</param>
    /// <param name="pageUrl">Page URL, used to resolve relative links.</param>
    /// <returns>Categories found; empty when the page has no recognizable menu.</returns>
    IReadOnlyList<DiscoveredCategory> Parse(string html, Uri pageUrl);
}
