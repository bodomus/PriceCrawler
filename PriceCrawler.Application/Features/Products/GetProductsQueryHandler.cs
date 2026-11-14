using PriceCrawler.Application.Features.Listing;
using PriceCrawler.Mediator;

namespace PriceCrawler.Application.Features.Products;

/// <summary>Reads a page of products through <see cref="IProductListReader"/>.</summary>
/// <param name="reader">Product list storage.</param>
public sealed class GetProductsQueryHandler(IProductListReader reader)
    : IQueryHandler<GetProductsQuery, PagedResult<ProductListItemDto>>
{
    /// <inheritdoc/>
    public Task<PagedResult<ProductListItemDto>> HandleAsync(GetProductsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ListSpec spec = request.List.Resolve(ProductListFields.Set);
        return reader.ReadAsync(request.Store, request.CategoryId, spec, cancellationToken);
    }
}
