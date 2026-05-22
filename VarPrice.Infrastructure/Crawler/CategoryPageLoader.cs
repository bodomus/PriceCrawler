using System.Net;

using Microsoft.Extensions.Logging;

namespace VarPrice.Infrastructure.Crawler;

public sealed class CategoryPageLoader(
    IHttpClientFactory httpClientFactory,
    VarusRequestCoordinator requestCoordinator,
    ILogger<CategoryPageLoader> logger) : ICategoryPageLoader
{
    public async Task<CategoryPageLoadResult> LoadAsync(CategorySeedUrl seed, Uri pageUrl, CancellationToken ct)
    {
        await requestCoordinator.AcquireRequestSlotAsync(ct);
        var http = httpClientFactory.CreateClient("varus");
        using var request = new HttpRequestMessage(HttpMethod.Get, pageUrl);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            var failureKind = ClassifyStatus(response.StatusCode);
            logger.LogWarning(
                "Category page skipped. Name={Name}; SeedUrl={SeedUrl}; PageUrl={PageUrl}; FailureKind={FailureKind}; HttpStatus={HttpStatus}",
                seed.Name,
                seed.Url,
                pageUrl,
                failureKind,
                (int)response.StatusCode);
            return CategoryPageLoadResult.Failed(failureKind);
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            const string failureKind = "CategoryPageInvalidContentType";
            logger.LogWarning(
                "Category page skipped. Name={Name}; SeedUrl={SeedUrl}; PageUrl={PageUrl}; FailureKind={FailureKind}; ContentType={ContentType}",
                seed.Name,
                seed.Url,
                pageUrl,
                failureKind,
                contentType);
            return CategoryPageLoadResult.Failed(failureKind);
        }

        var html = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(html))
        {
            const string failureKind = "CategoryPageEmptyBody";
            logger.LogWarning(
                "Category page skipped. Name={Name}; SeedUrl={SeedUrl}; PageUrl={PageUrl}; FailureKind={FailureKind}",
                seed.Name,
                seed.Url,
                pageUrl,
                failureKind);
            return CategoryPageLoadResult.Failed(failureKind);
        }

        return CategoryPageLoadResult.Ok(html);
    }

    private static string ClassifyStatus(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.NotFound => "CategoryPageNotFound",
            HttpStatusCode.Forbidden => "CategoryPageForbidden",
            HttpStatusCode.TooManyRequests => "CategoryPageRateLimited",
            _ when (int)statusCode >= 500 => "CategoryPageServerError",
            _ => "CategoryPageHttpError"
        };
}
