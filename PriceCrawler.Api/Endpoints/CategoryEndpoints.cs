using Microsoft.AspNetCore.Http.HttpResults;

using PriceCrawler.Application.Features.Categories;
using PriceCrawler.Mediator;

namespace PriceCrawler.Api.Endpoints;

/// <summary>Endpoints of the per-store category tree.</summary>
internal static class CategoryEndpoints
{
    /// <summary>Maps <c>GET {group}/stores/{store}/categories</c>.</summary>
    /// <param name="group">The versioned API route group.</param>
    /// <returns>The same route group.</returns>
    public static RouteGroupBuilder MapCategoryEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder categories = group
            .MapGroup("/stores/{store}/categories")
            .WithTags("Categories");

        categories.MapGet("/", GetChildrenAsync)
            .WithName("GetCategoryChildren")
            .WithSummary("Returns one level of a store's category tree.")
            .WithDescription(
                "Without parentId returns the top-level categories; with parentId returns the children of " +
                "that category. Only active categories are returned, in the order the store site shows them.")
            .ProducesValidationProblem();

        return group;
    }

    /// <summary>Returns one level of a store's category tree.</summary>
    /// <param name="store">Store key, for example <c>varus</c>.</param>
    /// <param name="parentId">Parent category id; omit it for the top level.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
    /// <returns>The categories of the level; empty when the store or the parent is unknown.</returns>
    private static async Task<Ok<IReadOnlyList<CategoryNodeDto>>> GetChildrenAsync(
        string store,
        long? parentId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CategoryNodeDto> nodes =
            await sender.SendAsync(new GetCategoryChildrenQuery(store, parentId), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(nodes);
    }
}
