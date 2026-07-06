namespace PriceCrawler.Application.Abstractions;

public interface IProductUrlDiscoveryStrategyFactory
{
    IProductUrlDiscoveryStrategy Create();
}
