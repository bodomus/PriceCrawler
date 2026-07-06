namespace PriceCrawler.Application.Models;

public sealed class ProductUrlDiscoveryUnavailableException(string message) : Exception(message);
