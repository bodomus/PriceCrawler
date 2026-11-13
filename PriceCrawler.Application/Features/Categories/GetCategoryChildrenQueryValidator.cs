using PriceCrawler.Mediator.Validation;

namespace PriceCrawler.Application.Features.Categories;

/// <summary>Checks the store key and the parent id of <see cref="GetCategoryChildrenQuery"/>.</summary>
public sealed class GetCategoryChildrenQueryValidator : IRequestValidator<GetCategoryChildrenQuery>
{
    /// <inheritdoc/>
    public IEnumerable<ValidationFailure> Validate(GetCategoryChildrenQuery request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!StoreKeys.IsValid(request.Store))
        {
            yield return new ValidationFailure(nameof(request.Store), StoreKeys.FormatMessage);
        }

        if (request.ParentId is <= 0)
        {
            yield return new ValidationFailure(nameof(request.ParentId), "Parent id must be a positive number.");
        }
    }
}
