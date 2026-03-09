using VarPrice.Application.Models;

namespace VarPrice.Application.Abstractions;

public interface IRunCrawlerUseCase
{
    Task<CrawlerRunResult> RunVegetablesAsync(CancellationToken ct);
}
