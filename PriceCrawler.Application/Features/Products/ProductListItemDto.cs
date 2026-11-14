namespace PriceCrawler.Application.Features.Products;

/// <summary>One product of a store list with its latest observed price.</summary>
/// <param name="ProductId">Product id; history and analytics endpoints use it.</param>
/// <param name="ExternalId">Product key on the store site (SKU), when known.</param>
/// <param name="Name">Product name.</param>
/// <param name="Url">Product page on the store site.</param>
/// <param name="PackValue">Pack size, e.g. <c>0.5</c>, when known.</param>
/// <param name="PackUnit">Pack unit, e.g. <c>л</c>, when known.</param>
/// <param name="Price">Latest price, or <see langword="null"/> when the last check found none.</param>
/// <param name="OldPrice">Regular price before a promotion, when there is one.</param>
/// <param name="IsPromo">Whether the latest price is a promotion.</param>
/// <param name="InStock">Whether the product was in stock at the latest check.</param>
/// <param name="CapturedAt">When the latest price was observed, or <see langword="null"/> without observations.</param>
public sealed record ProductListItemDto(
    long ProductId,
    string? ExternalId,
    string Name,
    string Url,
    decimal? PackValue,
    string? PackUnit,
    decimal? Price,
    decimal? OldPrice,
    bool IsPromo,
    bool InStock,
    DateTimeOffset? CapturedAt);
