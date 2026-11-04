using PriceCrawler.Domain.Categories;

namespace PriceCrawler.Infrastructure.Crawler;

/// <summary>Parses the varus.ua category menu. Interim: markup sample pending, returns no categories.</summary>
public sealed class VarusCategoryMenuParser : IVarusCategoryMenuParser
{
    /// <inheritdoc />
    public IReadOnlyList<DiscoveredCategory> Parse(string html, Uri pageUrl) => [];
}
