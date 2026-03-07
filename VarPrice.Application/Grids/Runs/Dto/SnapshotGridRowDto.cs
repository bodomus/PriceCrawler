using System.Text.Json.Serialization;

namespace VarPrice.Application.Grids.Runs.Dto;

public sealed class SnapshotGridRowDto
{
    [JsonPropertyName("id")] public long Id { get; init; }

    [JsonPropertyName("createdAtUtc")] public DateTime CreatedAtUtc { get; init; }

    [JsonPropertyName("city")] public string? City { get; init; }

    [JsonPropertyName("price")] public decimal Price { get; init; }

    [JsonPropertyName("oldPrice")] public decimal? OldPrice { get; init; }

    [JsonPropertyName("discountPercent")] public decimal? DiscountPercent { get; init; }

    [JsonPropertyName("promoFlag")] public bool PromoFlag { get; init; }

    [JsonPropertyName("inStock")] public bool? InStock { get; init; }
}
