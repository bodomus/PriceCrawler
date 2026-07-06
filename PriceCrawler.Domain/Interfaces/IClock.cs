namespace PriceCrawler.Domain.Interfaces;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
