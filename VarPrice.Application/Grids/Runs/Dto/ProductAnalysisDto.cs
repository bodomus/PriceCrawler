using System.Text.Json.Serialization;

namespace VarPrice.Application.Grids.Runs.Dto;

public sealed class ProductAnalysisDto
{
    [JsonPropertyName("snapshotId")] public long SnapshotId { get; init; }

    [JsonPropertyName("productCard")] public ProductDetailsDto? ProductCard { get; init; }

    [JsonPropertyName("analytics")] public ProductAnalyticsDto Analytics { get; init; } = new();

    [JsonPropertyName("history")]
    public IReadOnlyList<ProductPriceHistoryRowDto> History { get; init; } = Array.Empty<ProductPriceHistoryRowDto>();
}
