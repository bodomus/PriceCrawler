using System.Text.Json.Serialization;

namespace VarPrice.Application.Grids.Runs.Dto;

public sealed class ProductLiveIssueDto
{
    [JsonPropertyName("stage")] public string Stage { get; init; } = string.Empty;

    [JsonPropertyName("errorCode")] public string ErrorCode { get; init; } = string.Empty;

    [JsonPropertyName("httpStatus")] public int? HttpStatus { get; init; }

    [JsonPropertyName("message")] public string? Message { get; init; }

    [JsonPropertyName("isTransient")] public bool IsTransient { get; init; }

    [JsonPropertyName("isCritical")] public bool IsCritical { get; init; }
}
