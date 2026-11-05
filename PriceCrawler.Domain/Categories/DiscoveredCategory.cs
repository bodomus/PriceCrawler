namespace PriceCrawler.Domain.Categories;

/// <summary>A category as it appears on the store site, before it is stored.</summary>
/// <param name="ExternalId">Stable key of the category on the store site (for Varus the URL path, e.g. <c>banani</c>).</param>
/// <param name="Name">Display name as the store shows it.</param>
/// <param name="Url">Absolute URL of the category listing.</param>
/// <param name="ParentExternalId">Key of the parent category, or <see langword="null"/> for a top-level category.</param>
/// <param name="SortOrder">Position among siblings, as on the site.</param>
public sealed record DiscoveredCategory(
    string ExternalId,
    string Name,
    string Url,
    string? ParentExternalId,
    int SortOrder);
