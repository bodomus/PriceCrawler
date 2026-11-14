using PriceCrawler.Application.Features.Listing;
using PriceCrawler.Mediator;
using PriceCrawler.Mediator.Validation;

namespace PriceCrawler.Application.Features.Products;

/// <summary>Reads a page of products through <see cref="IProductListReader"/>.</summary>
/// <param name="reader">Product list storage.</param>
public sealed class GetProductsQueryHandler(IProductListReader reader) : IQueryHandler<GetProductsQuery, ProductPageDto>
{
    /// <inheritdoc/>
    public Task<ProductPageDto> HandleAsync(GetProductsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ListSpec spec = request.List.Resolve(ProductListFields.Set);
        if (!ProductGroupings.TryParse(request.GroupBy, out ProductGrouping grouping))
        {
            throw new RequestValidationException(
                "Grouping is invalid.",
                [new ValidationFailure(nameof(request.GroupBy), GetProductsQueryValidator.GroupByMessage)]);
        }

        return reader.ReadAsync(new ProductListRequest(request.Store, request.CategoryId, grouping, spec), cancellationToken);
    }
}
