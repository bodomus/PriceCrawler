namespace PriceCrawler.Domain.Models;

public sealed record ProductCatalogItem(
    long Id,
    string Source,
    string Url,
    string NormalizedUrl,
    string? ExternalId,
    string? Slug,
    DateTimeOffset FirstDiscoveredAtUtc,
    DateTimeOffset LastDiscoveredAtUtc,
    DateTimeOffset? LastCheckedAtUtc,
    DateTimeOffset? NextCheckAtUtc,
    bool IsActive,
    int ConsecutiveErrors);
