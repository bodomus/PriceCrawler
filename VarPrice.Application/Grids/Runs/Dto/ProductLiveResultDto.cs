using System.Text.Json.Serialization;

namespace VarPrice.Application.Grids.Runs.Dto;

public sealed class ProductLiveResultDto
{
    [JsonPropertyName("snapshotId")] public long SnapshotId { get; init; }

    [JsonPropertyName("requestedAtUtc")] public DateTime RequestedAtUtc { get; init; }

    [JsonPropertyName("requestedUrl")] public string RequestedUrl { get; init; } = string.Empty;

    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;

    [JsonPropertyName("httpStatus")] public int? HttpStatus { get; init; }

    [JsonPropertyName("latencyMs")] public long LatencyMs { get; init; }

    [JsonPropertyName("approximateRps")] public double ApproximateRps { get; init; }

    [JsonPropertyName("liveCard")] public ProductLiveCardDto? LiveCard { get; init; }

    [JsonPropertyName("issue")] public ProductLiveIssueDto? Issue { get; init; }
}
