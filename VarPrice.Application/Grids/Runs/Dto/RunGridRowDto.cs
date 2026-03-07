using System.Text.Json.Serialization;

namespace VarPrice.Application.Grids.Runs.Dto;

public sealed class RunGridRowDto
{
    [JsonPropertyName("id")] public long Id { get; init; }

    [JsonPropertyName("startedAtUtc")] public DateTime StartedAtUtc { get; init; }

    [JsonPropertyName("finishedAtUtc")] public DateTime? FinishedAtUtc { get; init; }

    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;

    [JsonPropertyName("itemsCount")] public int ItemsCount { get; init; }
}
