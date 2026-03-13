using VarPrice.Domain.Models;

namespace VarPrice.Domain.Interfaces;

public interface IPriceSnapshotRepository
{
    Task<ProductObservationWriteResult> StoreObservationAsync(
        long runId,
        long? queueId,
        ProductObservation observation,
        CancellationToken ct);

    Task<long> InsertProductErrorAsync(ProductErrorRecord error, CancellationToken ct);
}
