using VarPrice.Application.Models;

namespace VarPrice.Application.Abstractions;

public interface IRefreshProductCatalogUseCase
{
    Task<RefreshProductCatalogResult> ExecuteAsync(CancellationToken ct);
}
