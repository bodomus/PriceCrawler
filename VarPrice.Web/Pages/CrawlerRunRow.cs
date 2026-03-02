namespace VarPrice.Web.Pages;

public sealed record CrawlerRunRow(
    long Id,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    string Status,
    int ItemsCount);
