namespace VarPrice.Application.Models;

public sealed record ProductCard(
    string ProductId,
    string Name,
    string Url,
    decimal Price,
    decimal? OldPrice,
    bool PromoFlag,
    bool? InStock,
    decimal? PackValue,
    string? PackUnit,
    string? City);
