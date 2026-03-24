namespace VarPrice.Domain.Entities;

public sealed record CrawlError(
    long Id,
    long RunId,
    long? QueueId,
    long? ProductId,
    string? Url,
    string? ErrorCode,
    int? HttpStatus,
    string? ErrorMessage,
    DateTimeOffset CreatedAtUtc);
