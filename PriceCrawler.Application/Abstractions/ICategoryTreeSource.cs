using PriceCrawler.Application.Models;
using PriceCrawler.Domain.Categories;

namespace PriceCrawler.Application.Abstractions;

/// <summary>Reads the current category tree of a store from its site.</summary>
public interface ICategoryTreeSource
{
    /// <summary>Loads and parses the category tree.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The validated tree, or <see langword="null"/> when the page could not be read or looks wrong.</returns>
    Task<CategoryTreeSnapshot?> GetTreeAsync(CancellationToken ct);
}

/// <summary>A discovery strategy that also learns which category listed which product.</summary>
public interface ICategoryAwareDiscoveryStrategy
{
    /// <summary>Category facts of the last <c>DiscoverAsync</c> call, or <see langword="null"/> before the first call.</summary>
    CategoryDiscovery? LastCategoryDiscovery { get; }
}
