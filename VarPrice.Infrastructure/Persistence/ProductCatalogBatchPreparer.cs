using VarPrice.Domain.Models;

namespace VarPrice.Infrastructure.Persistence;

internal static class ProductCatalogBatchPreparer
{
    public static IReadOnlyList<ProductCatalogPreparedUpsertItem> Prepare(
        IReadOnlyCollection<ProductCatalogUpsertItem> items)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var selected = new Dictionary<ProductCatalogKey, ProductCatalogPreparedUpsertItem>();
        var index = 0;

        foreach (var item in items)
        {
            var source = TrimRequired(item.Source, 50);
            var url = TrimRequired(item.Url, 1024);
            var normalizedUrl = TrimRequired(item.NormalizedUrl, 1024);
            if (source.Length == 0 || url.Length == 0 || normalizedUrl.Length == 0)
            {
                index++;
                continue;
            }

            var prepared = new ProductCatalogPreparedUpsertItem(
                source,
                url,
                normalizedUrl,
                TrimNullable(item.ExternalId, 200),
                TrimNullable(item.Slug, 300),
                item.DiscoveredAtUtc.ToUniversalTime(),
                index);

            var key = new ProductCatalogKey(source, normalizedUrl);
            if (!selected.TryGetValue(key, out var existing)
                || prepared.DiscoveredAtUtc > existing.DiscoveredAtUtc
                || (prepared.DiscoveredAtUtc == existing.DiscoveredAtUtc &&
                    prepared.OriginalIndex > existing.OriginalIndex))
            {
                selected[key] = prepared;
            }

            index++;
        }

        return selected.Values
            .OrderBy(x => x.OriginalIndex)
            .ToArray();
    }

    private static string TrimRequired(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? TrimNullable(string? value, int maxLength)
    {
        var trimmed = TrimRequired(value, maxLength);
        return trimmed.Length == 0 ? null : trimmed;
    }

    private readonly record struct ProductCatalogKey(string Source, string NormalizedUrl)
    {
        public bool Equals(ProductCatalogKey other)
            => string.Equals(Source, other.Source, StringComparison.OrdinalIgnoreCase)
               && string.Equals(NormalizedUrl, other.NormalizedUrl, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode()
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(Source),
                StringComparer.OrdinalIgnoreCase.GetHashCode(NormalizedUrl));
    }
}

internal sealed record ProductCatalogPreparedUpsertItem(
    string Source,
    string Url,
    string NormalizedUrl,
    string? ExternalId,
    string? Slug,
    DateTimeOffset DiscoveredAtUtc,
    int OriginalIndex);
