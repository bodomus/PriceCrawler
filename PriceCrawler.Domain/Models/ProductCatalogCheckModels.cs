namespace PriceCrawler.Domain.Models;

public sealed record ProductCatalogCheckSuccess(
    long CatalogItemId,
    DateTimeOffset CheckedAtUtc,
    DateTimeOffset NextCheckAtUtc,
    string? ExternalId,
    string? Slug);

public sealed record ProductCatalogCheckFailure(
    long CatalogItemId,
    DateTimeOffset AttemptedAtUtc,
    DateTimeOffset NextCheckAtUtc);
