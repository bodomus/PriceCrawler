namespace VarPrice.Domain.Models;

public sealed record ProductObservation(
    string ProductId,
    string Name,
    string Url,
    decimal? PackValue,
    string? PackUnit,
    string? City,
    decimal? RegularPrice,
    decimal? FinalPrice,
    int? DiscountPercent,
    bool PromoFlag,
    bool? InStock,
    DateTimeOffset ObservedAtUtc)
{
    public bool HasMinimalValidState =>
        !string.IsNullOrWhiteSpace(ProductId) &&
        (RegularPrice.HasValue || FinalPrice.HasValue || InStock.HasValue);
}
