namespace VarPrice.Web.Pages;

public sealed record PriceSnapshotRow(
    long Id,
    long RunId,
    DateTime CapturedAtUtc,
    string? City,
    decimal Price,
    decimal? OldPrice,
    bool PromoFlag,
    bool? InStock);
