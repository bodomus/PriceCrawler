namespace VarPrice.Domain.Interfaces;

public interface IPriceSnapshotRepository
{
    Task<long> UpsertProductAsync(string productId, string name, string url, decimal? packValue, string? packUnit,
        CancellationToken ct);

    Task InsertSnapshotAsync(long runId, long productKey, string? city, decimal price, decimal? oldPrice,
        bool promoFlag, bool? inStock, long? queueId, CancellationToken ct);

    Task InsertProductErrorAsync(long runId, long? productKey, string? city, decimal price, decimal? oldPrice,
        bool promoFlag, bool? inStock, CancellationToken ct);

    Task InsertProductErrorAsync(long runId, string url, string errorCode, int? httpStatus, string? message,
        CancellationToken ct);

    Task InsertProductErrorAsync(long runId, long? queueId, string url, string errorCode, int? httpStatus,
        string? message, CancellationToken ct);
}
