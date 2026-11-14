namespace PriceCrawler.Application.Features.Listing;

/// <summary>Comparison of a filter condition. The query-string token is the lowercase name, e.g. <c>gte</c>.</summary>
public enum FilterOperator
{
    /// <summary>Equal.</summary>
    Eq,

    /// <summary>Not equal.</summary>
    Ne,

    /// <summary>Greater than.</summary>
    Gt,

    /// <summary>Greater than or equal.</summary>
    Gte,

    /// <summary>Less than.</summary>
    Lt,

    /// <summary>Less than or equal.</summary>
    Lte,

    /// <summary>Text contains the value, ignoring case.</summary>
    Contains,

    /// <summary>Text starts with the value, ignoring case.</summary>
    StartsWith,
}

/// <summary>Direction of a sort term.</summary>
public enum SortDirection
{
    /// <summary>Smallest first; written as the bare field name, e.g. <c>name</c>.</summary>
    Ascending,

    /// <summary>Largest first; written with a leading minus, e.g. <c>-price</c>.</summary>
    Descending,
}
