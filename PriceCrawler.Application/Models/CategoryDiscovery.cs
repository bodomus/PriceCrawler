using PriceCrawler.Domain.Categories;

namespace PriceCrawler.Application.Models;

/// <summary>Category facts collected while discovering product URLs.</summary>
/// <param name="Tree">The category tree read from the store, or <see langword="null"/> when it was not available.</param>
/// <param name="Listings">Products found on each crawled category.</param>
public sealed record CategoryDiscovery(
    CategoryTreeSnapshot? Tree,
    IReadOnlyList<CategoryProductListing> Listings);
