namespace PriceCrawler.Application.Models;

public sealed record IngestionRunResult(long IngestionRunId, string Status, string? ErrorCode, string? ErrorMessage);
