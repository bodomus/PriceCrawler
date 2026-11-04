namespace PriceCrawler.Domain.Categories;

/// <summary>
/// A validated, complete category tree of one store at one moment. Every parent precedes its
/// children, keys are unique and every referenced parent exists, so the tree has no cycles.
/// </summary>
public sealed class CategoryTreeSnapshot
{
    private CategoryTreeSnapshot(string source, IReadOnlyList<DiscoveredCategory> nodes, int duplicateCount)
    {
        Source = source;
        Nodes = nodes;
        DuplicateCount = duplicateCount;
        var parents = nodes
            .Where(node => node.ParentExternalId is not null)
            .Select(node => node.ParentExternalId!)
            .ToHashSet(StringComparer.Ordinal);
        Leaves = nodes.Where(node => !parents.Contains(node.ExternalId)).ToList();
    }

    /// <summary>Store the tree belongs to, e.g. <c>varus</c>.</summary>
    public string Source { get; }

    /// <summary>All categories, parents before children.</summary>
    public IReadOnlyList<DiscoveredCategory> Nodes { get; }

    /// <summary>Categories without children; their listings hold the products.</summary>
    public IReadOnlyList<DiscoveredCategory> Leaves { get; }

    /// <summary>How many repeated entries were dropped (the first occurrence of a key wins).</summary>
    public int DuplicateCount { get; }

    /// <summary>Builds a snapshot from categories in site order.</summary>
    /// <param name="source">Store key, e.g. <c>varus</c>.</param>
    /// <param name="categories">Categories in the order the site lists them; a parent must come before its children.</param>
    /// <returns>The validated snapshot.</returns>
    /// <exception cref="ArgumentException">
    /// The source or a required field is blank, a category is its own parent, or a parent is unknown
    /// or listed after its child.
    /// </exception>
    public static CategoryTreeSnapshot Create(string source, IEnumerable<DiscoveredCategory> categories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(categories);

        var nodes = new List<DiscoveredCategory>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = 0;
        foreach (var raw in categories)
        {
            var node = Normalize(raw);
            if (!known.Add(node.ExternalId))
            {
                duplicates++;
                continue;
            }

            if (node.ParentExternalId == node.ExternalId)
            {
                throw new ArgumentException($"Category '{node.ExternalId}' is its own parent.", nameof(categories));
            }

            if (node.ParentExternalId is not null && !known.Contains(node.ParentExternalId))
            {
                throw new ArgumentException(
                    $"Category '{node.ExternalId}' refers to parent '{node.ParentExternalId}' that is not listed before it.",
                    nameof(categories));
            }

            nodes.Add(node);
        }

        return new CategoryTreeSnapshot(source.Trim(), nodes, duplicates);
    }

    private static DiscoveredCategory Normalize(DiscoveredCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(category.ExternalId, nameof(category));
        ArgumentException.ThrowIfNullOrWhiteSpace(category.Name, nameof(category));
        ArgumentException.ThrowIfNullOrWhiteSpace(category.Url, nameof(category));
        var parent = string.IsNullOrWhiteSpace(category.ParentExternalId) ? null : category.ParentExternalId.Trim();
        return category with
        {
            ExternalId = category.ExternalId.Trim(),
            Name = category.Name.Trim(),
            Url = category.Url.Trim(),
            ParentExternalId = parent
        };
    }
}
