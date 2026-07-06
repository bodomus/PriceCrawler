using PriceCrawler.Application.Models;

namespace PriceCrawler.Application.Abstractions;

public interface IRefreshProductCatalogUseCase
{
    Task<RefreshProductCatalogResult> ExecuteAsync(CancellationToken ct);
}
