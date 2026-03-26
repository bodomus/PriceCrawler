using System.Text.Json.Serialization;

namespace VarPrice.Application.Grids.Runs.Dto;

public sealed class ProductAnalyticsPointDto
{
    [JsonPropertyName("snapshotId")] public long SnapshotId { get; init; }

    [JsonPropertyName("runId")] public long RunId { get; init; }

    [JsonPropertyName("capturedAtUtc")] public DateTime CapturedAtUtc { get; init; }

    [JsonPropertyName("price")] public decimal? Price { get; init; }

    [JsonPropertyName("oldPrice")] public decimal? OldPrice { get; init; }

    [JsonPropertyName("discountPercent")] public decimal? DiscountPercent { get; init; }

    [JsonPropertyName("promoFlag")] public bool PromoFlag { get; init; }

    [JsonPropertyName("inStock")] public bool InStock { get; init; }

    [JsonPropertyName("source")] public string Source { get; init; } = string.Empty;
}
