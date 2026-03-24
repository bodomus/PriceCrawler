namespace VarPrice.Domain.Models;

public sealed record ProductObservation(
    string? ExternalId,
    string Name,
    string Url,
    string? Slug,
    decimal? PackValue,
    string? PackUnit,
    decimal? Price,
    decimal? OldPrice,
    bool PromoFlag,
    bool InStock,
    DateTimeOffset ObservedAtUtc)
{
    public bool HasMinimalValidState =>
        !string.IsNullOrWhiteSpace(Url) &&
        (Price.HasValue || OldPrice.HasValue || InStock);
}
