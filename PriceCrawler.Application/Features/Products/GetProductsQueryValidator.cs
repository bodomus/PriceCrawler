using PriceCrawler.Mediator.Validation;

namespace PriceCrawler.Application.Features.Products;

/// <summary>Checks the store, the category and the list parameters of <see cref="GetProductsQuery"/>.</summary>
public sealed class GetProductsQueryValidator : IRequestValidator<GetProductsQuery>
{
    /// <inheritdoc/>
    public IEnumerable<ValidationFailure> Validate(GetProductsQuery request)
    {
        ArgumentNullException.ThrowIfNull(request);
        List<ValidationFailure> failures = [];

        if (!StoreKeys.IsValid(request.Store))
        {
            failures.Add(new ValidationFailure(nameof(request.Store), StoreKeys.FormatMessage));
        }

        if (request.CategoryId is <= 0)
        {
            failures.Add(new ValidationFailure(nameof(request.CategoryId), "Category id must be a positive number."));
        }

        request.List.TryResolve(ProductListFields.Set, out IReadOnlyList<ValidationFailure> listFailures);
        failures.AddRange(listFailures);
        return failures;
    }
}
