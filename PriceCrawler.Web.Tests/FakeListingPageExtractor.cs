using PriceCrawler.Application.Abstractions;
using PriceCrawler.Application.Models;

namespace PriceCrawler.Web.Tests;

internal sealed class FakeListingPageExtractor(
    ListingExtractionResult? result = null) : IListingPageExtractor
{
    public Task<ListingExtractionResult> ExtractAsync(string url, CancellationToken ct) =>
        Task.FromResult(result ?? ListingExtractionResult.Success(url, [], 200, 0, 0d));
}
