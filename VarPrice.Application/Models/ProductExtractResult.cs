namespace VarPrice.Application.Models;

public sealed record ProductExtractResult(
    ProductCard? Card,
    ProductExtractIssue? Issue,
    int? HttpStatus,
    long LatencyMs,
    double ApproximateRps)
{
    public bool IsSuccess => Card is not null && Issue is null;

    public bool HasCard => Card is not null;

    public bool HasIssue => Issue is not null;

    public bool IsCriticalFailure => Card is null && Issue is not null;

    public string? ErrorCode => Issue?.ErrorCode;

    public bool IsTransient => Issue?.IsTransient ?? false;

    public static ProductExtractResult Success(ProductCard card, int? httpStatus, long latencyMs, double approximateRps)
        => new(card, null, httpStatus, latencyMs, approximateRps);

    public static ProductExtractResult Fail(
        string errorCode,
        int? httpStatus,
        string? message,
        long latencyMs,
        double approximateRps,
        bool isTransient,
        string stage = "extract",
        string? detailsJson = null)
        => new(
            null,
            new ProductExtractIssue(stage, errorCode, httpStatus, message, detailsJson, isTransient, true),
            httpStatus,
            latencyMs,
            approximateRps);

    public static ProductExtractResult Partial(
        ProductCard card,
        string errorCode,
        int? httpStatus,
        string? message,
        long latencyMs,
        double approximateRps,
        string stage = "extract",
        string? detailsJson = null)
        => new(
            card,
            new ProductExtractIssue(stage, errorCode, httpStatus, message, detailsJson, false, false),
            httpStatus,
            latencyMs,
            approximateRps);
}
