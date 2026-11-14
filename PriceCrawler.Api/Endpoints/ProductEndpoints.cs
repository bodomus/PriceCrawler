using Microsoft.AspNetCore.Http.HttpResults;

using PriceCrawler.Application.Features.Listing;
using PriceCrawler.Application.Features.Products;
using PriceCrawler.Mediator;

namespace PriceCrawler.Api.Endpoints;

/// <summary>Endpoints of a store's product list.</summary>
internal static class ProductEndpoints
{
    /// <summary>Maps <c>GET {group}/stores/{store}/products</c>.</summary>
    /// <param name="group">The versioned API route group.</param>
    /// <returns>The same route group.</returns>
    public static RouteGroupBuilder MapProductEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/stores/{store}/products", GetProductsAsync)
            .WithTags("Products")
            .WithName("GetProducts")
            .WithSummary("Returns a page of a store's products with their latest prices.")
            .WithDescription(
                "Paging: page (from 1), pageSize (1-200, default 50). " +
                "Sorting: sort=-price,name (minus for descending; fields: name, price, oldPrice, isPromo, inStock, capturedAt, priceChange, priceChangePercent; default name). " +
                "Filtering: repeat filter=field:operator:value, all must hold, e.g. filter=name:contains:молоко&filter=price:lte:50. " +
                "Operators: text eq, ne, contains, startswith; numbers and dates eq, ne, gt, gte, lt, lte; booleans eq, ne. " +
                "categoryId limits the list to a category and all its subcategories. " +
                "Day change: previousPrice is the last price before the Kyiv day of the latest observation began; " +
                "priceChange and priceChangePercent (sortable and filterable) compare the latest price with it. " +
                "trend holds the daily closing prices of the 30 days up to the latest observation, for the mini chart. " +
                "groupBy=" + ProductGroupings.Names + " sorts by the group first, marks rows with groupKey and returns all groups with counts.")
            .ProducesValidationProblem();

        return group;
    }

    /// <summary>Returns a page of a store's products.</summary>
    /// <param name="store">Store key, for example <c>varus</c>.</param>
    /// <param name="categoryId">Only this category and its subcategories.</param>
    /// <param name="page">1-based page number; default 1.</param>
    /// <param name="pageSize">Rows per page, 1 to 200; default 50.</param>
    /// <param name="groupBy">Grouping: <c>category</c>, <c>promo</c>, <c>stock</c> or <c>change</c>; none by default.</param>
    /// <param name="sort">Comma-separated sort fields, minus for descending, e.g. <c>-price,name</c>.</param>
    /// <param name="filter">Conditions <c>field:operator:value</c>; repeat the parameter for several.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
    /// <returns>The page with the total count and the groups.</returns>
    private static async Task<Ok<ProductPageDto>> GetProductsAsync(
        string store,
        long? categoryId,
        int? page,
        int? pageSize,
        string? groupBy,
        string? sort,
        string[]? filter,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var query = new GetProductsQuery(store, categoryId, groupBy, new ListParameters(page, pageSize, sort, filter));
        ProductPageDto result = await sender.SendAsync(query, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result);
    }
}
