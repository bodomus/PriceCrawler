using PriceCrawler.Application.Features.Listing;
using PriceCrawler.Mediator;

namespace PriceCrawler.Application.Features.Products;

/// <summary>Reads a page of a store's active products with their latest prices.</summary>
/// <param name="Store">Store key, e.g. <c>varus</c>.</param>
/// <param name="CategoryId">Only this category and its subcategories, or <see langword="null"/> for all products.</param>
/// <param name="List">Paging, sorting and filtering over <see cref="ProductListFields.Set"/>.</param>
public sealed record GetProductsQuery(string Store, long? CategoryId, ListParameters List)
    : IQuery<PagedResult<ProductListItemDto>>;
