using PriceCrawler.Application.Models;

namespace PriceCrawler.Application.Abstractions;

public interface IProductUrlDiscoveryService
{
    Task<ProductUrlDiscoveryResult> DiscoverProductUrlsAsync(CancellationToken ct);
}
