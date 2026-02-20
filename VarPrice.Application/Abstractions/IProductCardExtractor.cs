using VarPrice.Application.Models;

namespace VarPrice.Application.Abstractions;

public interface IProductCardExtractor
{
    Task<ProductCard?> ExtractAsync(string url, CancellationToken ct);
}
