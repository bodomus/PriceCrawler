namespace VarPrice.Domain.Entities;

public sealed record Product(
    long Id,
    string? ExternalId,
    string Name,
    string Url,
    string? Slug,
    decimal? PackValue,
    string? PackUnit,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
