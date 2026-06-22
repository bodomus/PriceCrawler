namespace VarPrice.Domain.Models;

public sealed record ProductCatalogUpsertItem(
    string Source,
    string Url,
    string NormalizedUrl,
    string? ExternalId,
    string? Slug,
    DateTimeOffset DiscoveredAtUtc);
