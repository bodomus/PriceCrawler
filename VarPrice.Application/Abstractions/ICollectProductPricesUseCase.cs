using VarPrice.Application.Models;

namespace VarPrice.Application.Abstractions;

public interface ICollectProductPricesUseCase
{
    Task<CollectProductPricesResult> ExecuteAsync(CancellationToken ct);
}
