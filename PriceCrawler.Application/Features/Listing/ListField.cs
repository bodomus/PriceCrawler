namespace PriceCrawler.Application.Features.Listing;

/// <summary>Value type of a list field; it decides how filter values are parsed and which operators apply.</summary>
public enum ListFieldKind
{
    /// <summary>A string: <c>eq</c>, <c>ne</c>, <c>contains</c>, <c>startswith</c> (the last two ignore case).</summary>
    Text,

    /// <summary>A decimal number: <c>eq</c>, <c>ne</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    Number,

    /// <summary><c>true</c> or <c>false</c>: <c>eq</c>, <c>ne</c>.</summary>
    Boolean,

    /// <summary>An ISO 8601 date and time: <c>eq</c>, <c>ne</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    DateTime,
}

/// <summary>One field of a list that clients may sort or filter by.</summary>
/// <param name="Name">Public field name, as used in <c>sort</c> and <c>filter</c> (camelCase, like the JSON).</param>
/// <param name="Kind">Value type of the field.</param>
/// <param name="Sortable">Whether the list can be sorted by the field.</param>
/// <param name="Filterable">Whether the list can be filtered by the field.</param>
public sealed record ListField(string Name, ListFieldKind Kind, bool Sortable = true, bool Filterable = true)
{
    /// <summary>Operators that apply to the field's kind.</summary>
    public IReadOnlyList<FilterOperator> Operators => Kind switch
    {
        ListFieldKind.Text => [FilterOperator.Eq, FilterOperator.Ne, FilterOperator.Contains, FilterOperator.StartsWith],
        ListFieldKind.Boolean => [FilterOperator.Eq, FilterOperator.Ne],
        _ => [FilterOperator.Eq, FilterOperator.Ne, FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte],
    };
}
