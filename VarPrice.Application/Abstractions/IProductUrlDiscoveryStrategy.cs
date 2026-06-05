using VarPrice.Application.Models;

namespace VarPrice.Application.Abstractions;

public interface IProductUrlDiscoveryStrategy
{
    ProductUrlDiscoverySourceKind SourceKind { get; }

    string SourceName { get; }

    Task<IReadOnlyCollection<ProductDiscoveryItem>> DiscoverAsync(CancellationToken ct);
}
