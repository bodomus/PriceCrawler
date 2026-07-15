using System.Diagnostics;
using System.Net;

using Microsoft.Extensions.Logging;

using PriceCrawler.Application.Abstractions;
using PriceCrawler.Application.Models;

namespace PriceCrawler.Infrastructure.Crawler;

public sealed class VarusListingPageExtractor(
    IHttpClientFactory httpClientFactory,
    VarusRequestCoordinator requestCoordinator,
    ICategoryProductLinkExtractor productLinkExtractor,
    ILogger<VarusListingPageExtractor> logger) : IListingPageExtractor
{
    public async Task<ListingExtractionResult> ExtractAsync(string url, CancellationToken ct)
    {
        await requestCoordinator.AcquireRequestSlotAsync(ct);

        var sw = Stopwatch.StartNew();
        int? httpStatus = null;
        var rps = requestCoordinator.GetApproximateRps();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var listingUri))
        {
            sw.Stop();
            return ListingExtractionResult.WithIssue(
                url,
                [],
                CrawlerErrorCodes.UnsupportedPageType,
                null,
                "Listing URL is not absolute.",
                sw.ElapsedMilliseconds,
                rps,
                false);
        }

        var http = httpClientFactory.CreateClient("varus");
        using var request = new HttpRequestMessage(HttpMethod.Get, listingUri);

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            httpStatus = (int)response.StatusCode;
            rps = requestCoordinator.GetApproximateRps();

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                sw.Stop();
                return ListingExtractionResult.WithIssue(
                    url,
                    [],
                    CrawlerErrorCodes.NotFound,
                    httpStatus,
                    $"HTTP {httpStatus}",
                    sw.ElapsedMilliseconds,
                    rps,
                    false);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                sw.Stop();
                return ListingExtractionResult.WithIssue(
                    url,
                    [],
                    CrawlerErrorCodes.TooManyRequests,
                    httpStatus,
                    $"HTTP {httpStatus}",
                    sw.ElapsedMilliseconds,
                    rps,
                    true);
            }

            if ((int)response.StatusCode >= 500)
            {
                sw.Stop();
                return ListingExtractionResult.WithIssue(
                    url,
                    [],
                    CrawlerErrorCodes.Http5xx,
                    httpStatus,
                    $"HTTP {httpStatus}",
                    sw.ElapsedMilliseconds,
                    rps,
                    true);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                sw.Stop();
                return ListingExtractionResult.WithIssue(
                    url,
                    [],
                    CrawlerErrorCodes.Unknown,
                    httpStatus,
                    $"HTTP {httpStatus}",
                    sw.ElapsedMilliseconds,
                    rps,
                    false);
            }

            var html = await response.Content.ReadAsStringAsync(ct);
            var urls = productLinkExtractor.ExtractProductUrls(html, listingUri)
                .Select(x => x.AbsoluteUri)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            sw.Stop();

            if (urls.Count == 0)
            {
                logger.LogWarning(
                    "Listing page contained no verified JSON-LD ItemList products url={Url} http_status={HttpStatus} latency_ms={LatencyMs} current_rps={CurrentRps:F2}",
                    url,
                    httpStatus,
                    sw.ElapsedMilliseconds,
                    rps);
                return ListingExtractionResult.WithIssue(
                    url,
                    [],
                    CrawlerErrorCodes.ListingNoProductsFound,
                    httpStatus,
                    "Listing page did not contain product links.",
                    sw.ElapsedMilliseconds,
                    rps,
                    false);
            }

            logger.LogInformation(
                "Listing page parsed url={Url} http_status={HttpStatus} latency_ms={LatencyMs} current_rps={CurrentRps:F2} found_product_links={FoundProductLinks}",
                url,
                httpStatus,
                sw.ElapsedMilliseconds,
                rps,
                urls.Count);
            logger.LogDebug(
                "Verified listing product link samples url={Url} samples={Samples}",
                url,
                string.Join(" | ", urls.Take(10)));
            return ListingExtractionResult.Success(url, urls, httpStatus, sw.ElapsedMilliseconds, rps);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            rps = requestCoordinator.GetApproximateRps();
            logger.LogWarning(ex, "Listing extractor error for {Url}", url);
            return ListingExtractionResult.WithIssue(
                url,
                [],
                CrawlerErrorCodes.Unknown,
                httpStatus,
                ex.Message,
                sw.ElapsedMilliseconds,
                rps,
                false);
        }
    }
}
