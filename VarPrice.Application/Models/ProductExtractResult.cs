namespace VarPrice.Application.Models;

public sealed record ProductExtractResult(
    ProductCard? Card,
    string? ErrorCode,
    int? HttpStatus,
    string? Message,
    long LatencyMs,
    double ApproximateRps,
    bool IsTransient)
{
    public bool IsSuccess => Card is not null;

    public static ProductExtractResult Success(ProductCard card, int? httpStatus, long latencyMs, double approximateRps)
        => new(card, null, httpStatus, null, latencyMs, approximateRps, false);

    public static ProductExtractResult Fail(
        string errorCode,
        int? httpStatus,
        string? message,
        long latencyMs,
        double approximateRps,
        bool isTransient)
        => new(null, errorCode, httpStatus, message, latencyMs, approximateRps, isTransient);
}
