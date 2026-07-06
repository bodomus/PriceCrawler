using PriceCrawler.Application.Models;

namespace PriceCrawler.Application.Abstractions;

public interface IProductCardExtractor
{
    Task<ProductExtractResult> ExtractAsync(string url, CancellationToken ct);
}
