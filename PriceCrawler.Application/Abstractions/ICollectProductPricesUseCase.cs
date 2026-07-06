using PriceCrawler.Application.Models;

namespace PriceCrawler.Application.Abstractions;

public interface ICollectProductPricesUseCase
{
    Task<CollectProductPricesResult> ExecuteAsync(CancellationToken ct);
}
