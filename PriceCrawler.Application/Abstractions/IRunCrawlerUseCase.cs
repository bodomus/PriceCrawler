using PriceCrawler.Application.Models;

namespace PriceCrawler.Application.Abstractions;

public interface IRunCrawlerUseCase
{
    Task<CrawlerRunResult> RunVegetablesAsync(CancellationToken ct);
}
