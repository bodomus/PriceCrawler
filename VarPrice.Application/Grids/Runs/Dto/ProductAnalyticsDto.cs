using System.Text.Json.Serialization;

namespace VarPrice.Application.Grids.Runs.Dto;

public sealed class ProductAnalyticsDto
{
    [JsonPropertyName("snapshotId")] public long SnapshotId { get; init; }

    [JsonPropertyName("historyPointsCount")]
    public int HistoryPointsCount { get; init; }

    [JsonPropertyName("pricePointsCount")] public int PricePointsCount { get; init; }

    [JsonPropertyName("promoMomentsCount")]
    public int PromoMomentsCount { get; init; }

    [JsonPropertyName("inStockMomentsCount")]
    public int InStockMomentsCount { get; init; }

    [JsonPropertyName("selectedCapturedAtUtc")]
    public DateTime? SelectedCapturedAtUtc { get; init; }

    [JsonPropertyName("firstCapturedAtUtc")]
    public DateTime? FirstCapturedAtUtc { get; init; }

    [JsonPropertyName("lastCapturedAtUtc")]
    public DateTime? LastCapturedAtUtc { get; init; }

    [JsonPropertyName("selectedPrice")] public decimal? SelectedPrice { get; init; }

    [JsonPropertyName("previousPrice")] public decimal? PreviousPrice { get; init; }

    [JsonPropertyName("firstObservedPrice")]
    public decimal? FirstObservedPrice { get; init; }

    [JsonPropertyName("latestObservedPrice")]
    public decimal? LatestObservedPrice { get; init; }

    [JsonPropertyName("minPrice")] public decimal? MinPrice { get; init; }

    [JsonPropertyName("maxPrice")] public decimal? MaxPrice { get; init; }

    [JsonPropertyName("averagePrice")] public decimal? AveragePrice { get; init; }

    [JsonPropertyName("priceSpread")] public decimal? PriceSpread { get; init; }

    [JsonPropertyName("changeFromPreviousAmount")]
    public decimal? ChangeFromPreviousAmount { get; init; }

    [JsonPropertyName("changeFromPreviousPercent")]
    public decimal? ChangeFromPreviousPercent { get; init; }

    [JsonPropertyName("changeFromFirstAmount")]
    public decimal? ChangeFromFirstAmount { get; init; }

    [JsonPropertyName("changeFromFirstPercent")]
    public decimal? ChangeFromFirstPercent { get; init; }

    [JsonPropertyName("points")]
    public IReadOnlyList<ProductAnalyticsPointDto> Points { get; init; } = Array.Empty<ProductAnalyticsPointDto>();
}
