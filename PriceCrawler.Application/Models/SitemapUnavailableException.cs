namespace PriceCrawler.Application.Models;

public sealed class SitemapUnavailableException(string message) : Exception(message);
