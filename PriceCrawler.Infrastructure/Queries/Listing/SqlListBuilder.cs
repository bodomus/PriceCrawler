using System.Globalization;
using System.Text;

using Npgsql;

using PriceCrawler.Application.Features.Listing;

namespace PriceCrawler.Infrastructure.Queries.Listing;

/// <summary>
/// Turns a validated <see cref="ListSpec"/> into SQL fragments for PostgreSQL. Field names map to
/// column expressions through a fixed whitelist, and every value is a parameter, so client input
/// never becomes SQL text.
/// </summary>
/// <param name="columns">Public field name to SQL column expression; must cover every field the spec can use.</param>
internal sealed class SqlListBuilder(IReadOnlyDictionary<string, string> columns)
{
    private readonly List<(string Name, object Value)> _parameters = [];

    /// <summary>Parameters created by <see cref="BuildWhere"/>.</summary>
    public IReadOnlyList<(string Name, object Value)> Parameters => _parameters;

    /// <summary>Builds the AND-ed filter conditions.</summary>
    /// <param name="filters">Validated filter terms.</param>
    /// <returns>A condition starting with <c>true</c>, safe to append after <c>where</c>.</returns>
    public string BuildWhere(IReadOnlyList<FilterTerm> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        var sql = new StringBuilder("true");
        foreach (FilterTerm filter in filters)
        {
            string column = Column(filter.Field);
            string parameter = AddParameter(filter);
            string condition = filter.Operator switch
            {
                FilterOperator.Eq => $"{column} = {parameter}",
                FilterOperator.Ne => $"{column} is distinct from {parameter}",
                FilterOperator.Gt => $"{column} > {parameter}",
                FilterOperator.Gte => $"{column} >= {parameter}",
                FilterOperator.Lt => $"{column} < {parameter}",
                FilterOperator.Lte => $"{column} <= {parameter}",
                FilterOperator.Contains or FilterOperator.StartsWith => $"{column} ilike {parameter} escape '\\'",
                _ => throw new ArgumentOutOfRangeException(nameof(filters), filter.Operator, "Unknown filter operator."),
            };
            sql.Append(" and ").Append(condition);
        }

        return sql.ToString();
    }

    /// <summary>Builds the order, with nulls last and a unique tie-breaker so pages are stable.</summary>
    /// <param name="sort">Validated sort terms.</param>
    /// <param name="tieBreaker">Unique column expression appended last, e.g. the id.</param>
    /// <returns>Text to append after <c>order by</c>.</returns>
    public string BuildOrderBy(IReadOnlyList<SortTerm> sort, string tieBreaker)
    {
        ArgumentNullException.ThrowIfNull(sort);
        IEnumerable<string> terms = sort.Select(term =>
            $"{Column(term.Field)} {(term.Direction == SortDirection.Descending ? "desc" : "asc")} nulls last");
        return string.Join(", ", [.. terms, $"{tieBreaker} asc"]);
    }

    /// <summary>Adds the parameters to a command.</summary>
    /// <param name="parameters">Parameter collection of a command.</param>
    public void AddTo(NpgsqlParameterCollection parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        foreach ((string name, object value) in _parameters)
        {
            parameters.AddWithValue(name, value);
        }
    }

    /// <summary>Returns the column expression of a field.</summary>
    /// <param name="field">Field from the list's field set.</param>
    /// <returns>Column expression.</returns>
    /// <exception cref="InvalidOperationException">The field has no column (a programming error).</exception>
    private string Column(ListField field) =>
        columns.TryGetValue(field.Name, out string? column)
            ? column
            : throw new InvalidOperationException($"List field '{field.Name}' has no SQL column.");

    /// <summary>Creates the parameter of a filter term, preparing LIKE patterns and UTC times.</summary>
    /// <param name="filter">Filter term.</param>
    /// <returns>Parameter placeholder, e.g. <c>@f0</c>.</returns>
    private string AddParameter(FilterTerm filter)
    {
        string name = "f" + _parameters.Count.ToString(CultureInfo.InvariantCulture);
        object value = filter switch
        {
            { Operator: FilterOperator.Contains, Value: string text } => "%" + EscapeLike(text) + "%",
            { Operator: FilterOperator.StartsWith, Value: string text } => EscapeLike(text) + "%",
            { Value: DateTimeOffset moment } => moment.ToUniversalTime(),
            _ => filter.Value,
        };
        _parameters.Add((name, value));
        return "@" + name;
    }

    /// <summary>Escapes LIKE wildcards so the value matches literally.</summary>
    /// <param name="value">Raw text.</param>
    /// <returns>Escaped text.</returns>
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
