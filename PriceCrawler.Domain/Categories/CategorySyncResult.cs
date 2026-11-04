namespace PriceCrawler.Domain.Categories;

/// <summary>What one tree synchronization changed in the stored tree.</summary>
/// <param name="ReceivedCount">Categories in the snapshot.</param>
/// <param name="InsertedCount">New categories.</param>
/// <param name="RenamedCount">Existing categories whose name, URL or position changed.</param>
/// <param name="MovedCount">Existing categories that got another parent.</param>
/// <param name="ReactivatedCount">Categories that were inactive and appeared again.</param>
/// <param name="DeactivatedCount">Active categories missing from the snapshot.</param>
public sealed record CategorySyncResult(
    int ReceivedCount,
    int InsertedCount,
    int RenamedCount,
    int MovedCount,
    int ReactivatedCount,
    int DeactivatedCount);

/// <summary>Products listed on one crawled category page set.</summary>
/// <param name="CategoryExternalId">Key of the crawled category.</param>
/// <param name="ProductUrls">Normalized product URLs found on its pages.</param>
/// <param name="IsComplete">
/// <see langword="true"/> when every page was read; only then are links of products that are no
/// longer listed removed.
/// </param>
public sealed record CategoryProductListing(
    string CategoryExternalId,
    IReadOnlyCollection<string> ProductUrls,
    bool IsComplete);

/// <summary>What one product-to-category linking pass changed.</summary>
/// <param name="LinkedCount">Links inserted or confirmed.</param>
/// <param name="RemovedCount">Links removed from completely crawled categories.</param>
/// <param name="UnknownProductCount">URLs that are not in the product catalog and were skipped.</param>
public sealed record CategoryLinkResult(int LinkedCount, int RemovedCount, int UnknownProductCount);

/// <summary>A stored category with what a tree view needs to render one level.</summary>
/// <param name="Id">Database id.</param>
/// <param name="ExternalId">Key on the store site.</param>
/// <param name="Name">Display name.</param>
/// <param name="Url">Category URL on the store site.</param>
/// <param name="ParentId">Parent id, or <see langword="null"/> for top level.</param>
/// <param name="HasChildren">Whether active child categories exist.</param>
/// <param name="ProductCount">Distinct active catalog items in this category and all its descendants.</param>
public sealed record CategoryNode(
    long Id,
    string ExternalId,
    string Name,
    string Url,
    long? ParentId,
    bool HasChildren,
    int ProductCount);
