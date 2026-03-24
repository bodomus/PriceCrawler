namespace VarPrice.Application.Models;

public sealed record ProductCard(
    string? ExternalId,
    string Name,
    string Url,
    string? Slug,
    decimal? Price,
    decimal? OldPrice,
    bool PromoFlag,
    bool InStock,
    decimal? PackValue,
    string? PackUnit);
