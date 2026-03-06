using VarPrice.Application.Models;

namespace VarPrice.Application.Abstractions;

public interface IProductCardExtractor
{
    Task<ProductExtractResult> ExtractAsync(string url, CancellationToken ct);
}
