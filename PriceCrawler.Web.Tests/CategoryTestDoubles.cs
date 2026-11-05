using PriceCrawler.Application.Abstractions;
using PriceCrawler.Domain.Categories;
using PriceCrawler.Domain.Interfaces;

namespace PriceCrawler.Web.Tests;

/// <summary>Tree source that always returns the same tree.</summary>
internal sealed class FixedCategoryTreeSource(CategoryTreeSnapshot? tree) : ICategoryTreeSource
{
    public int Calls { get; private set; }

    public Task<CategoryTreeSnapshot?> GetTreeAsync(CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(tree);
    }
}

/// <summary>In-memory category repository that records what the use case asked for.</summary>
internal sealed class FakeCategoryRepository : ICategoryRepository
{
    public int ActiveCount { get; init; }

    public bool ThrowOnSync { get; init; }

    public List<CategoryTreeSnapshot> SyncedTrees { get; } = [];

    public List<IReadOnlyCollection<CategoryProductListing>> LinkedListings { get; } = [];

    public Task<CategorySyncResult> SyncTreeAsync(
        CategoryTreeSnapshot tree,
        long refreshId,
        DateTimeOffset seenAtUtc,
        CancellationToken ct)
    {
        if (ThrowOnSync)
        {
            throw new InvalidOperationException("sync failed");
        }

        SyncedTrees.Add(tree);
        return Task.FromResult(new CategorySyncResult(tree.Nodes.Count, tree.Nodes.Count, 0, 0, 0, 0));
    }

    public Task<int> GetActiveCountAsync(string source, CancellationToken ct) => Task.FromResult(ActiveCount);

    public Task<CategoryLinkResult> LinkProductsAsync(
        string source,
        IReadOnlyCollection<CategoryProductListing> listings,
        long refreshId,
        DateTimeOffset seenAtUtc,
        CancellationToken ct)
    {
        LinkedListings.Add(listings);
        return Task.FromResult(new CategoryLinkResult(listings.Sum(listing => listing.ProductUrls.Count), 0, 0));
    }

    public Task<IReadOnlyList<CategoryNode>> GetChildrenAsync(string source, long? parentId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CategoryNode>>([]);
}
