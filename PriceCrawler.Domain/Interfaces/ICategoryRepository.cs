using PriceCrawler.Domain.Categories;

namespace PriceCrawler.Domain.Interfaces;

/// <summary>Stores per-store category trees and the products listed in each category.</summary>
public interface ICategoryRepository
{
    /// <summary>
    /// Makes the stored tree of <see cref="CategoryTreeSnapshot.Source"/> match the snapshot: adds new
    /// categories, updates names and parents, reactivates returning ones and deactivates missing ones.
    /// Rows are never deleted.
    /// </summary>
    /// <param name="tree">Complete validated tree of one store.</param>
    /// <param name="refreshId">Catalog refresh that read the tree, or 0 when there is none.</param>
    /// <param name="seenAtUtc">Time the tree was read.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Counts of what changed.</returns>
    Task<CategorySyncResult> SyncTreeAsync(
        CategoryTreeSnapshot tree,
        long refreshId,
        DateTimeOffset seenAtUtc,
        CancellationToken ct);

    /// <summary>Counts active categories of a store.</summary>
    /// <param name="source">Store key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of active categories.</returns>
    Task<int> GetActiveCountAsync(string source, CancellationToken ct);

    /// <summary>
    /// Links catalog items to the categories whose pages listed them. For complete listings, links of
    /// items that are no longer listed are removed.
    /// </summary>
    /// <param name="source">Store key.</param>
    /// <param name="listings">Products found per crawled category.</param>
    /// <param name="refreshId">Catalog refresh that crawled the pages, or 0 when there is none.</param>
    /// <param name="seenAtUtc">Time the pages were read.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Counts of what changed.</returns>
    Task<CategoryLinkResult> LinkProductsAsync(
        string source,
        IReadOnlyCollection<CategoryProductListing> listings,
        long refreshId,
        DateTimeOffset seenAtUtc,
        CancellationToken ct);

    /// <summary>Returns the active children of a category, in site order.</summary>
    /// <param name="source">Store key.</param>
    /// <param name="parentId">Parent category id, or <see langword="null"/> for top-level categories.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One tree level.</returns>
    Task<IReadOnlyList<CategoryNode>> GetChildrenAsync(string source, long? parentId, CancellationToken ct);
}
