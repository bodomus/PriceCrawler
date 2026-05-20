using VarPrice.Application.Models;

namespace VarPrice.Application.Abstractions;

public interface IProductUrlDiscoveryService
{
    Task<ProductUrlDiscoveryResult> DiscoverProductUrlsAsync(CancellationToken ct);
}
