using System.Text.Json.Serialization;

namespace VarPrice.Application.Grids.Runs.Dto;

public sealed class ProductGridRowDto
{
    [JsonPropertyName("id")] public long Id { get; init; }

    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    [JsonPropertyName("sku")] public string Sku { get; init; } = string.Empty;

    [JsonPropertyName("url")] public string Url { get; init; } = string.Empty;

    [JsonPropertyName("price")] public decimal Price { get; init; }

    [JsonPropertyName("unit")] public string? Unit { get; init; }

    [JsonPropertyName("updatedAtUtc")] public DateTime UpdatedAtUtc { get; init; }
}
