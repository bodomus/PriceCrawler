using PriceCrawler.Application.Features.Listing;

namespace PriceCrawler.Application.Features.Products;

/// <summary>Read-side port: pages of a store's products with their latest prices.</summary>
public interface IProductListReader
{
    /// <summary>Reads one page of the active products of a store.</summary>
    /// <param name="store">Store key, e.g. <c>varus</c>.</param>
    /// <param name="categoryId">
    /// Only products of this category and all its subcategories, or <see langword="null"/> for all.
    /// </param>
    /// <param name="spec">Validated page, order and filter over <see cref="ProductListFields.Set"/>.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page and the total number of matching products.</returns>
    Task<PagedResult<ProductListItemDto>> ReadAsync(
        string store,
        long? categoryId,
        ListSpec spec,
        CancellationToken cancellationToken);
}
