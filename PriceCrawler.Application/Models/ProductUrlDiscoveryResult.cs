namespace PriceCrawler.Application.Models;

/// <summary>Result of product URL discovery.</summary>
/// <param name="SourceKind">Which discovery source produced the URLs.</param>
/// <param name="Urls">Product URLs after filtering.</param>
/// <param name="Categories">Category tree and per-category listings, when the source knows them.</param>
public sealed record ProductUrlDiscoveryResult(
    ProductUrlDiscoverySourceKind SourceKind,
    IReadOnlyList<string> Urls,
    CategoryDiscovery? Categories = null);

public enum ProductUrlDiscoverySourceKind
{
    Sitemap,
    CategorySeed,
    Api
}
