namespace VarPrice.Application.Models;

public sealed record ProductUrlDiscoveryResult(
    ProductUrlDiscoverySourceKind SourceKind,
    IReadOnlyList<string> Urls);

public enum ProductUrlDiscoverySourceKind
{
    Sitemap,
    CategorySeed
}
