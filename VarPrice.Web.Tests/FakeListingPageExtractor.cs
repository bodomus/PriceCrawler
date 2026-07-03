using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;

namespace VarPrice.Web.Tests;

internal sealed class FakeListingPageExtractor(
    ListingExtractionResult? result = null) : IListingPageExtractor
{
    public Task<ListingExtractionResult> ExtractAsync(string url, CancellationToken ct) =>
        Task.FromResult(result ?? ListingExtractionResult.Success(url, [], 200, 0, 0d));
}
