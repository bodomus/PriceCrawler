namespace VarPrice.Application.Models;

public sealed record CrawlerRunResult(long RunId, string Status, int ProductsProcessed, int Errors, string? Note = null);
