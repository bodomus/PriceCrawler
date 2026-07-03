using VarPrice.Application.Models;

namespace VarPrice.Application.Abstractions;

public interface IListingPageExtractor
{
    Task<ListingExtractionResult> ExtractAsync(string url, CancellationToken ct);
}
