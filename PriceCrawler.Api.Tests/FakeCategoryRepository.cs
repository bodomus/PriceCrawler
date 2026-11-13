using PriceCrawler.Domain.Categories;
using PriceCrawler.Domain.Interfaces;

namespace PriceCrawler.Api.Tests;

/// <summary>In-memory category reads; writes are not used by the API.</summary>
public sealed class FakeCategoryRepository : ICategoryRepository
{
    public List<(string Source, CategoryNode Node)> Nodes { get; } = [];

    public Task<IReadOnlyList<CategoryNode>> GetChildrenAsync(string source, long? parentId, CancellationToken ct)
    {
        IReadOnlyList<CategoryNode> level =
        [
            .. Nodes
                .Where(entry => entry.Source == source && entry.Node.ParentId == parentId)
                .Select(entry => entry.Node),
        ];
        return Task.FromResult(level);
    }

    public Task<CategorySyncResult> SyncTreeAsync(CategoryTreeSnapshot tree, long refreshId, DateTimeOffset seenAtUtc, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<int> GetActiveCountAsync(string source, CancellationToken ct) => throw new NotSupportedException();

    public Task<CategoryLinkResult> LinkProductsAsync(
        string source,
        IReadOnlyCollection<CategoryProductListing> listings,
        long refreshId,
        DateTimeOffset seenAtUtc,
        CancellationToken ct) =>
        throw new NotSupportedException();
}
