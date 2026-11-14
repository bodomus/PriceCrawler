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
/// <param name="PreviousPrice">
/// Last price observed before the Kyiv day of <paramref name="CapturedAt"/> began (the previous day's close),
/// or <see langword="null"/> when there is none.
/// </param>
/// <param name="PriceChange"><paramref name="Price"/> minus <paramref name="PreviousPrice"/>, when both are known.</param>
/// <param name="PriceChangePercent">The change in percent of <paramref name="PreviousPrice"/>, rounded to 0.01.</param>
/// <param name="Trend">
/// Daily closing prices (last observation of each Kyiv day) of the 30 days up to <paramref name="CapturedAt"/>,
/// oldest first; days without observations are skipped. For the mini chart.
/// </param>
/// <param name="GroupKey">Key of the row's group when the list is grouped, otherwise <see langword="null"/>.</param>
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
    DateTimeOffset? CapturedAt,
    decimal? PreviousPrice,
    decimal? PriceChange,
    decimal? PriceChangePercent,
    IReadOnlyList<decimal> Trend,
    string? GroupKey);
