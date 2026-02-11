namespace VarPrice.Web.Crawler;

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
    string? City
);

public sealed record CrawlerRunResult(long RunId, string Status, int ProductsProcessed, int Errors, string? Note = null);
