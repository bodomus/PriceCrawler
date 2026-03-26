using System.Text.Json.Serialization;

namespace VarPrice.Application.Grids.Runs.Dto;

public sealed class ProductLiveCardDto
{
    [JsonPropertyName("sku")] public string? Sku { get; init; }

    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    [JsonPropertyName("url")] public string Url { get; init; } = string.Empty;

    [JsonPropertyName("slug")] public string? Slug { get; init; }

    [JsonPropertyName("unit")] public string? Unit { get; init; }

    [JsonPropertyName("currentPrice")] public decimal? CurrentPrice { get; init; }

    [JsonPropertyName("oldPrice")] public decimal? OldPrice { get; init; }

    [JsonPropertyName("promoFlag")] public bool PromoFlag { get; init; }

    [JsonPropertyName("inStock")] public bool InStock { get; init; }
}
