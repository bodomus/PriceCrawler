namespace VarPrice.Web.Pages;

public sealed record IngestionRunRow(
    long Id,
    long CrawlerRunId,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    string Status,
    string? ErrorCode);
