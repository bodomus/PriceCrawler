namespace VarPrice.Infrastructure.Crawler;

public sealed record CategoryPageLoadResult(bool Success, string? Html, string? FailureKind)
{
    public static CategoryPageLoadResult Ok(string html) => new(true, html, null);

    public static CategoryPageLoadResult Failed(string failureKind) => new(false, null, failureKind);
}
