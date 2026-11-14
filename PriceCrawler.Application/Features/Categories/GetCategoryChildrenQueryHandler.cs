using PriceCrawler.Domain.Categories;
using PriceCrawler.Domain.Interfaces;
using PriceCrawler.Mediator;

namespace PriceCrawler.Application.Features.Categories;

/// <summary>Reads one level of a store's category tree from <see cref="ICategoryRepository"/>.</summary>
/// <param name="categories">Category storage.</param>
public sealed class GetCategoryChildrenQueryHandler(ICategoryRepository categories)
    : IQueryHandler<GetCategoryChildrenQuery, IReadOnlyList<CategoryNodeDto>>
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<CategoryNodeDto>> HandleAsync(
        GetCategoryChildrenQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<CategoryNode> nodes =
            await categories.GetChildrenAsync(request.Store, request.ParentId, cancellationToken).ConfigureAwait(false);
        return [.. nodes.Select(ToDto)];
    }

    /// <summary>Maps a stored category to the API shape.</summary>
    /// <param name="node">Stored category.</param>
    /// <returns>The API shape.</returns>
    private static CategoryNodeDto ToDto(CategoryNode node) =>
        new(node.Id, node.ExternalId, node.Name, node.Url, node.ParentId, node.HasChildren, node.ProductCount);
}
