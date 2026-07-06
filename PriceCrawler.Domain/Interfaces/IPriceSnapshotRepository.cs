using PriceCrawler.Domain.Models;

namespace PriceCrawler.Domain.Interfaces;

public interface IPriceSnapshotRepository
{
    Task<ProductObservationWriteResult> StoreObservationAsync(
        long runId,
        long? queueId,
        ProductObservation observation,
        CancellationToken ct);

    Task<long> InsertCrawlErrorAsync(CrawlErrorRecord error, CancellationToken ct);
}
