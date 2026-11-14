namespace PriceCrawler.Application.Features.Listing;

/// <summary>One sort term of a list request.</summary>
/// <param name="Field">Field to sort by.</param>
/// <param name="Direction">Sort direction.</param>
public sealed record SortTerm(ListField Field, SortDirection Direction);

/// <summary>One filter condition of a list request; all conditions must hold (AND).</summary>
/// <param name="Field">Field to compare.</param>
/// <param name="Operator">Comparison.</param>
/// <param name="Value">Parsed value: <see cref="string"/>, <see cref="decimal"/>, <see cref="bool"/> or <see cref="DateTimeOffset"/>, matching the field kind.</param>
public sealed record FilterTerm(ListField Field, FilterOperator Operator, object Value);

/// <summary>A validated page, order and filter of a list: what a data source needs to run the query.</summary>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
/// <param name="Sort">Sort terms, most significant first; never empty.</param>
/// <param name="Filters">Filter conditions, all of which must hold.</param>
public sealed record ListSpec(int Page, int PageSize, IReadOnlyList<SortTerm> Sort, IReadOnlyList<FilterTerm> Filters)
{
    /// <summary>Rows to skip before the page.</summary>
    public int Offset => (Page - 1) * PageSize;
}

/// <summary>One page of a list with the total count of matching rows.</summary>
/// <typeparam name="T">Row type.</typeparam>
/// <param name="Items">Rows of the page.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Requested rows per page.</param>
/// <param name="TotalCount">Rows matching the filter on all pages.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    /// <summary>Number of pages; 0 when nothing matches.</summary>
    public long TotalPages => TotalCount == 0 ? 0 : ((TotalCount - 1) / PageSize) + 1;
}
