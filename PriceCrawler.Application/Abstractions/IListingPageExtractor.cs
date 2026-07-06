using PriceCrawler.Application.Models;

namespace PriceCrawler.Application.Abstractions;

public interface IListingPageExtractor
{
    Task<ListingExtractionResult> ExtractAsync(string url, CancellationToken ct);
}
