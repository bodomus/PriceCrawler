using System.Text.Json.Serialization;

namespace VarPrice.Web.ViewModels.Runs;

public sealed class RunTreeNodeVm
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    [JsonPropertyName("parentId")] public string? ParentId { get; init; }

    [JsonPropertyName("nodeType")] public string NodeType { get; init; } = string.Empty;

    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;

    [JsonPropertyName("runId")] public long? RunId { get; init; }

    [JsonPropertyName("snapshotScope")] public string SnapshotScope { get; init; } = SnapshotScopes.None;

    [JsonPropertyName("startedAtUtc")] public DateTime? StartedAtUtc { get; init; }

    [JsonPropertyName("finishedAtUtc")] public DateTime? FinishedAtUtc { get; init; }

    [JsonPropertyName("status")] public string? Status { get; init; }

    [JsonPropertyName("itemsCount")] public int? ItemsCount { get; init; }
}

public static class SnapshotScopes
{
    public const string None = "none";
    public const string All = "all";
    public const string Successful = "successful";
    public const string Failed = "failed";
}
