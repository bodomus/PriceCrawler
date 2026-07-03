namespace VarPrice.Application.Models;

public sealed record ListingExtractionResult(
    string SourceUrl,
    IReadOnlyList<string> ProductUrls,
    ProductExtractIssue? Issue,
    int? HttpStatus,
    long LatencyMs,
    double ApproximateRps)
{
    public int FoundCount => ProductUrls.Count;

    public string? ErrorCode => Issue?.ErrorCode;

    public bool IsTransient => Issue?.IsTransient ?? false;

    public static ListingExtractionResult Success(
        string sourceUrl,
        IReadOnlyList<string> productUrls,
        int? httpStatus,
        long latencyMs,
        double approximateRps)
        => new(sourceUrl, productUrls, null, httpStatus, latencyMs, approximateRps);

    public static ListingExtractionResult WithIssue(
        string sourceUrl,
        IReadOnlyList<string> productUrls,
        string errorCode,
        int? httpStatus,
        string? message,
        long latencyMs,
        double approximateRps,
        bool isTransient,
        string stage = "listing")
        => new(
            sourceUrl,
            productUrls,
            new ProductExtractIssue(stage, errorCode, httpStatus, message, null, isTransient, true),
            httpStatus,
            latencyMs,
            approximateRps);
}
