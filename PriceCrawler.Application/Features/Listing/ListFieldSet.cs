namespace PriceCrawler.Application.Features.Listing;

/// <summary>The fields one list exposes for sorting and filtering, plus its default order.</summary>
public sealed class ListFieldSet
{
    private readonly Dictionary<string, ListField> _byName;

    /// <summary>Creates the set.</summary>
    /// <param name="fields">Fields of the list; names are matched ignoring case.</param>
    /// <param name="defaultSort">Order used when the client gives none; its fields must be in <paramref name="fields"/>.</param>
    /// <exception cref="ArgumentException">Two fields share a name, or a default sort field is not in the set.</exception>
    public ListFieldSet(IReadOnlyList<ListField> fields, IReadOnlyList<SortTerm> defaultSort)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(defaultSort);

        _byName = new Dictionary<string, ListField>(StringComparer.OrdinalIgnoreCase);
        foreach (ListField field in fields)
        {
            if (!_byName.TryAdd(field.Name, field))
            {
                throw new ArgumentException($"Field '{field.Name}' is listed twice.", nameof(fields));
            }
        }

        if (defaultSort.Any(term => !_byName.ContainsKey(term.Field.Name)))
        {
            throw new ArgumentException("Every default sort field must be in the set.", nameof(defaultSort));
        }

        Fields = fields;
        DefaultSort = defaultSort;
    }

    /// <summary>All fields, in declaration order.</summary>
    public IReadOnlyList<ListField> Fields { get; }

    /// <summary>Order used when the client gives none.</summary>
    public IReadOnlyList<SortTerm> DefaultSort { get; }

    /// <summary>Finds a field by its public name, ignoring case.</summary>
    /// <param name="name">Field name.</param>
    /// <param name="field">The field, when found.</param>
    /// <returns><see langword="true"/> when the list has the field.</returns>
    public bool TryGet(string name, out ListField field) => _byName.TryGetValue(name, out field!);
}
