using System.Globalization;

using PriceCrawler.Mediator.Validation;

namespace PriceCrawler.Application.Features.Listing;

/// <summary>
/// Paging, sorting and filtering of a list as the client sends them (our replacement for Kendo
/// <c>DataSourceRequest</c>):
/// <list type="bullet">
/// <item><c>page</c> — 1-based, default 1;</item>
/// <item><c>pageSize</c> — 1 to <see cref="MaxPageSize"/>, default <see cref="DefaultPageSize"/>;</item>
/// <item><c>sort</c> — comma-separated fields, <c>-</c> for descending: <c>-price,name</c>;</item>
/// <item><c>filter</c> — repeatable <c>field:operator:value</c>, all must hold: <c>name:contains:молоко</c>, <c>price:lte:50</c>.</item>
/// </list>
/// </summary>
/// <param name="Page">Requested page, or <see langword="null"/> for the first.</param>
/// <param name="PageSize">Requested page size, or <see langword="null"/> for the default.</param>
/// <param name="Sort">Sort expression, or <see langword="null"/> for the list's default order.</param>
/// <param name="Filter">Filter conditions; <see langword="null"/> or empty for none.</param>
public sealed record ListParameters(int? Page, int? PageSize, string? Sort, IReadOnlyList<string>? Filter)
{
    /// <summary>Page size when the client gives none.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>Largest allowed page size.</summary>
    public const int MaxPageSize = 200;

    /// <summary>Largest number of sort terms or filter conditions.</summary>
    public const int MaxTerms = 10;

    /// <summary>Parses and checks the parameters against the fields of a list.</summary>
    /// <param name="fields">Fields the list exposes.</param>
    /// <param name="failures">Broken rules; empty on success.</param>
    /// <returns>The validated specification, or <see langword="null"/> when there are failures.</returns>
    public ListSpec? TryResolve(ListFieldSet fields, out IReadOnlyList<ValidationFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(fields);
        List<ValidationFailure> errors = [];

        int page = Page ?? 1;
        if (page < 1)
        {
            errors.Add(new ValidationFailure(nameof(Page), "Page must be 1 or greater."));
        }

        int pageSize = PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize)
        {
            errors.Add(new ValidationFailure(nameof(PageSize), $"Page size must be between 1 and {MaxPageSize}."));
        }

        List<SortTerm> sort = ParseSort(fields, errors);
        List<FilterTerm> filters = ParseFilters(fields, errors);

        failures = errors;
        return errors.Count == 0 ? new ListSpec(page, pageSize, sort, filters) : null;
    }

    /// <summary>Like <see cref="TryResolve"/>, but throws on invalid input (use after validation has run).</summary>
    /// <param name="fields">Fields the list exposes.</param>
    /// <returns>The validated specification.</returns>
    /// <exception cref="RequestValidationException">The parameters are invalid.</exception>
    public ListSpec Resolve(ListFieldSet fields) =>
        TryResolve(fields, out IReadOnlyList<ValidationFailure> failures)
        ?? throw new RequestValidationException("List parameters are invalid.", failures);

    /// <summary>Parses <see cref="Sort"/>; the list's default order is used when it is empty.</summary>
    /// <param name="fields">Fields the list exposes.</param>
    /// <param name="errors">Collects broken rules.</param>
    /// <returns>Sort terms, never empty when there are no errors.</returns>
    private List<SortTerm> ParseSort(ListFieldSet fields, List<ValidationFailure> errors)
    {
        string[] tokens = (Sort ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return [.. fields.DefaultSort];
        }

        if (tokens.Length > MaxTerms)
        {
            errors.Add(new ValidationFailure(nameof(Sort), $"At most {MaxTerms} sort fields are allowed."));
            return [];
        }

        List<SortTerm> terms = [];
        foreach (string token in tokens)
        {
            bool descending = token.StartsWith('-');
            string name = descending ? token[1..] : token;
            if (!fields.TryGet(name, out ListField field) || !field.Sortable)
            {
                errors.Add(new ValidationFailure(nameof(Sort), $"Cannot sort by '{name}'. Sortable fields: {SortableNames(fields)}."));
                continue;
            }

            terms.Add(new SortTerm(field, descending ? SortDirection.Descending : SortDirection.Ascending));
        }

        return terms;
    }

    /// <summary>Parses <see cref="Filter"/> conditions of the form <c>field:operator:value</c>.</summary>
    /// <param name="fields">Fields the list exposes.</param>
    /// <param name="errors">Collects broken rules.</param>
    /// <returns>Parsed conditions.</returns>
    private List<FilterTerm> ParseFilters(ListFieldSet fields, List<ValidationFailure> errors)
    {
        IReadOnlyList<string> conditions = Filter ?? [];
        if (conditions.Count > MaxTerms)
        {
            errors.Add(new ValidationFailure(nameof(Filter), $"At most {MaxTerms} filter conditions are allowed."));
            return [];
        }

        List<FilterTerm> terms = [];
        foreach (string condition in conditions)
        {
            // The value may itself contain ':', so only the first two separators split.
            string[] parts = condition.Split(':', 3);
            if (parts.Length != 3)
            {
                errors.Add(new ValidationFailure(nameof(Filter), $"'{condition}' is not 'field:operator:value'."));
                continue;
            }

            if (!fields.TryGet(parts[0].Trim(), out ListField field) || !field.Filterable)
            {
                errors.Add(new ValidationFailure(nameof(Filter), $"Cannot filter by '{parts[0]}'. Filterable fields: {FilterableNames(fields)}."));
                continue;
            }

            string opToken = parts[1].Trim();
            if (opToken.Length == 0
                || !opToken.All(char.IsAsciiLetter)
                || !Enum.TryParse(opToken, ignoreCase: true, out FilterOperator op)
                || !field.Operators.Contains(op))
            {
                string allowed = string.Join(", ", field.Operators.Select(item => item.ToString().ToLowerInvariant()));
                errors.Add(new ValidationFailure(nameof(Filter), $"Operator '{parts[1]}' does not apply to '{field.Name}'. Allowed: {allowed}."));
                continue;
            }

            object? value = ParseValue(field.Kind, parts[2]);
            if (value is null)
            {
                errors.Add(new ValidationFailure(nameof(Filter), $"'{parts[2]}' is not a valid {field.Kind} value for '{field.Name}'."));
                continue;
            }

            terms.Add(new FilterTerm(field, op, value));
        }

        return terms;
    }

    /// <summary>Parses a filter value for a field kind, culture-invariant.</summary>
    /// <param name="kind">Field kind.</param>
    /// <param name="text">Raw value.</param>
    /// <returns>The parsed value, or <see langword="null"/> when it does not parse or text is empty.</returns>
    private static object? ParseValue(ListFieldKind kind, string text) => kind switch
    {
        ListFieldKind.Text => text.Length is > 0 and <= 200 ? text : null,
        ListFieldKind.Number => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number) ? number : null,
        ListFieldKind.Boolean => bool.TryParse(text, out bool flag) ? flag : null,
        ListFieldKind.DateTime => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset moment) ? moment : null,
        _ => null,
    };

    /// <summary>Names of the sortable fields, for error messages.</summary>
    /// <param name="fields">Fields the list exposes.</param>
    /// <returns>Comma-separated names.</returns>
    private static string SortableNames(ListFieldSet fields) =>
        string.Join(", ", fields.Fields.Where(field => field.Sortable).Select(field => field.Name));

    /// <summary>Names of the filterable fields, for error messages.</summary>
    /// <param name="fields">Fields the list exposes.</param>
    /// <returns>Comma-separated names.</returns>
    private static string FilterableNames(ListFieldSet fields) =>
        string.Join(", ", fields.Fields.Where(field => field.Filterable).Select(field => field.Name));
}
