namespace VarPrice.Domain.Interfaces;

public interface IPriceSnapshotRepository
{
    Task<long> UpsertProductAsync(string productId, string name, string url, decimal? packValue, string? packUnit, CancellationToken ct);
    Task InsertSnapshotAsync(long runId, long productKey, string? city, decimal price, decimal? oldPrice, bool promoFlag, bool? inStock, CancellationToken ct);
    Task InsertProductErrorAsync(long runId, long? productKey, string? city, decimal price, decimal? oldPrice, bool promoFlag, bool? inStock, CancellationToken ct);
}
