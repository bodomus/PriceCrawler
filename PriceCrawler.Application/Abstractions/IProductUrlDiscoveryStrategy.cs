using PriceCrawler.Application.Models;

namespace PriceCrawler.Application.Abstractions;

public interface IProductUrlDiscoveryStrategy
{
    ProductUrlDiscoverySourceKind SourceKind { get; }

    string SourceName { get; }

    Task<IReadOnlyCollection<ProductDiscoveryItem>> DiscoverAsync(CancellationToken ct);
}
