namespace VarPrice.Domain.Models;

public sealed record CrawlErrorRecord(
    long RunId,
    long? QueueId,
    long? ProductId,
    string? Url,
    DateTimeOffset CreatedAtUtc,
    string? ErrorCode,
    int? HttpStatus,
    string? ErrorMessage);
