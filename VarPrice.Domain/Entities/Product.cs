namespace VarPrice.Domain.Entities;

public sealed record Product(
    long ProductKey,
    string ProductId,
    string Name,
    string Url,
    decimal? PackValue,
    string? PackUnit,
    DateTimeOffset? LastSeenAt);
