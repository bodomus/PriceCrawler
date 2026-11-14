namespace PriceCrawler.Application.Features.Products;

/// <summary>One group of a grouped product list, over all pages.</summary>
/// <param name="Key">
/// Group key that rows carry in <see cref="ProductListItemDto.GroupKey"/>: a category id for
/// <see cref="ProductGrouping.Category"/>, otherwise one of the <see cref="ProductGroupings"/> keys.
/// </param>
/// <param name="Name">Category name for category groups, otherwise <see langword="null"/> (the client names the group).</param>
/// <param name="Count">Products of the group matching the filter.</param>
public sealed record ProductGroupDto(string Key, string? Name, long Count);

/// <summary>One page of a store's product list.</summary>
/// <param name="Items">Rows of the page; when grouped, rows of one group are contiguous and groups follow <paramref name="Groups"/>.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Requested rows per page.</param>
/// <param name="TotalCount">Products matching the filter on all pages.</param>
/// <param name="Groups">All groups in display order when the list is grouped, otherwise empty.</param>
public sealed record ProductPageDto(
    IReadOnlyList<ProductListItemDto> Items,
    int Page,
    int PageSize,
    long TotalCount,
    IReadOnlyList<ProductGroupDto> Groups)
{
    /// <summary>Number of pages; 0 when nothing matches.</summary>
    public long TotalPages => TotalCount == 0 ? 0 : ((TotalCount - 1) / PageSize) + 1;
}
