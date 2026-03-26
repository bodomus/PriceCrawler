using System.Text.Json.Serialization;

namespace VarPrice.Application.Grids.Runs.Dto;

public sealed class ProductDetailsDto
{
    [JsonPropertyName("id")] public long Id { get; init; }

    [JsonPropertyName("snapshotId")] public long SnapshotId { get; init; }

    [JsonPropertyName("runId")] public long RunId { get; init; }

    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    [JsonPropertyName("sku")] public string? Sku { get; init; }

    [JsonPropertyName("url")] public string Url { get; init; } = string.Empty;

    [JsonPropertyName("slug")] public string? Slug { get; init; }

    [JsonPropertyName("unit")] public string? Unit { get; init; }

    [JsonPropertyName("currentPrice")] public decimal? CurrentPrice { get; init; }

    [JsonPropertyName("oldPrice")] public decimal? OldPrice { get; init; }

    [JsonPropertyName("discountPercent")] public decimal? DiscountPercent { get; init; }

    [JsonPropertyName("promoFlag")] public bool PromoFlag { get; init; }

    [JsonPropertyName("inStock")] public bool InStock { get; init; }

    [JsonPropertyName("updatedAtUtc")] public DateTime? UpdatedAtUtc { get; init; }

    [JsonPropertyName("capturedAtUtc")] public DateTime? CapturedAtUtc { get; init; }

    [JsonPropertyName("source")] public string? Source { get; init; }

    [JsonPropertyName("brand")] public string? Brand { get; init; }

    [JsonPropertyName("category")] public string? Category { get; init; }

    [JsonPropertyName("imageUrl")] public string? ImageUrl { get; init; }
}
