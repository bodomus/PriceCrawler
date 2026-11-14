using PriceCrawler.Application.Features.Listing;

namespace PriceCrawler.Application.Features.Products;

/// <summary>What to read from <see cref="IProductListReader"/>.</summary>
/// <param name="Store">Store key, e.g. <c>varus</c>.</param>
/// <param name="CategoryId">Only products of this category and all its subcategories, or <see langword="null"/> for all.</param>
/// <param name="Grouping">How to group the rows.</param>
/// <param name="Spec">Validated page, order and filter over <see cref="ProductListFields.Set"/>.</param>
public sealed record ProductListRequest(string Store, long? CategoryId, ProductGrouping Grouping, ListSpec Spec);

/// <summary>Read-side port: pages of a store's products with their latest prices.</summary>
public interface IProductListReader
{
    /// <summary>Reads one page of the active products of a store.</summary>
    /// <param name="request">Store, category, grouping, page, order and filter.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page, the total number of matching products and the groups.</returns>
    Task<ProductPageDto> ReadAsync(ProductListRequest request, CancellationToken cancellationToken);
}
