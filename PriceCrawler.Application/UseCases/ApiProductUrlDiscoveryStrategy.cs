using PriceCrawler.Application.Abstractions;
using PriceCrawler.Application.Models;

namespace PriceCrawler.Application.UseCases;

public sealed class ApiProductUrlDiscoveryStrategy : IProductUrlDiscoveryStrategy
{
    public ProductUrlDiscoverySourceKind SourceKind => ProductUrlDiscoverySourceKind.Api;

    public string SourceName => "api";

    public Task<IReadOnlyCollection<ProductDiscoveryItem>> DiscoverAsync(CancellationToken ct)
    {
        throw new NotSupportedException(
            "Crawler:DiscoveryMode=Api is reserved for future Varus API discovery and is not implemented yet.");
    }
}
