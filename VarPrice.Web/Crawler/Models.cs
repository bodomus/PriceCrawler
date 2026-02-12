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

public sealed class CrawlerRunResult
{
    public long RunId { get; set; }
    public string Status { get; set; } = "unknown";
    public int ProductsProcessed { get; set; }
    public int Errors { get; set; }
    public string? Note { get; set; }

    public int SitemapsDiscovered { get; set; }
    public int UrlsDiscovered { get; set; }
    public int ProductUrlsDiscovered { get; set; }
    public int PagesFetched { get; set; }
    public int ItemsParsed { get; set; }
    public int ItemsSaved { get; set; }
    public string? LastError { get; set; }
}
