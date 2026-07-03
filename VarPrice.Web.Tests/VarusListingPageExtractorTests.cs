using System.Net;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using VarPrice.Application.Models;
using VarPrice.Domain.Enums;
using VarPrice.Infrastructure.Crawler;

namespace VarPrice.Web.Tests;

public sealed class VarusListingPageExtractorTests
{
    [Theory]
    [InlineData("https://varus.ua/kovbasi~brand_espana")]
    [InlineData("https://varus.ua/kovbasi~brand_gremio-de-la-carne")]
    public void Classify_BrandFilterUrl_ReturnsListingPage(string url)
    {
        Assert.Equal(QueueItemKind.ListingPage, VarusPageKindClassifier.Classify(url));
    }

    [Fact]
    public async Task ExtractAsync_WhenListingContainsMultipleProductCards_ReturnsNormalizedProductUrls()
    {
        await using var harness = CreateHarness(
            """
            <html><body>
              <div class="product-card"><a href="/product-a?utm=1#card">A</a></div>
              <div class="product-card"><a href="https://varus.ua/product-b">B</a></div>
              <div class="product-card"><a href="https://example.com/not-varus">External</a></div>
            </body></html>
            """);

        var result = await harness.Extractor.ExtractAsync(
            "https://varus.ua/kovbasi~brand_espana",
            CancellationToken.None);

        Assert.Null(result.Issue);
        Assert.Equal(
            [
                "https://varus.ua/product-a",
                "https://varus.ua/product-b"
            ],
            result.ProductUrls.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ExtractAsync_WhenListingContainsOneProductCard_ReturnsSingleProductUrl()
    {
        await using var harness = CreateHarness(
            """
            <html><body>
              <div class="product-card"><a href="/single-product">Only one</a></div>
            </body></html>
            """);

        var result = await harness.Extractor.ExtractAsync(
            "https://varus.ua/kovbasi~brand_gremio-de-la-carne",
            CancellationToken.None);

        Assert.Null(result.Issue);
        Assert.Equal(["https://varus.ua/single-product"], result.ProductUrls);
    }

    [Fact]
    public async Task ExtractAsync_DeduplicatesProductUrls()
    {
        await using var harness = CreateHarness(
            """
            <html><body>
              <div class="product-card"><a href="/same-product?tracking=1">Same</a></div>
              <div class="product-card"><a href="https://varus.ua/same-product#duplicate">Same duplicate</a></div>
            </body></html>
            """);

        var result = await harness.Extractor.ExtractAsync(
            "https://varus.ua/kovbasi~brand_espana",
            CancellationToken.None);

        Assert.Null(result.Issue);
        Assert.Equal(["https://varus.ua/same-product"], result.ProductUrls);
    }

    [Fact]
    public async Task ExtractAsync_WhenNoProducts_ReturnsListingNoProductsFound()
    {
        await using var harness = CreateHarness("<html><body>No product cards here</body></html>");

        var result = await harness.Extractor.ExtractAsync(
            "https://varus.ua/kovbasi~brand_espana",
            CancellationToken.None);

        Assert.Equal(CrawlerErrorCodes.ListingNoProductsFound, result.ErrorCode);
        Assert.False(result.IsTransient);
    }

    private static ExtractorHarness CreateHarness(string html)
    {
        var handler = new StaticHtmlMessageHandler(html);
        var httpClient = new HttpClient(handler);
        var crawlerOptions = Options.Create(new CrawlerOptions
        {
            RequestsPerSecond = 100d,
            MaxConcurrency = 4
        });
        var coordinator = new VarusRequestCoordinator(crawlerOptions, NullLogger<VarusRequestCoordinator>.Instance);
        var extractor = new VarusListingPageExtractor(
            new StubHttpClientFactory(httpClient),
            coordinator,
            new CategoryProductLinkExtractor(),
            NullLogger<VarusListingPageExtractor>.Instance);

        return new ExtractorHarness(httpClient, coordinator, extractor);
    }

    private sealed class StubHttpClientFactory(HttpClient httpClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => httpClient;
    }

    private sealed class StaticHtmlMessageHandler(string html) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html"),
                RequestMessage = request
            });
    }

    private sealed class ExtractorHarness(
        HttpClient httpClient,
        VarusRequestCoordinator coordinator,
        VarusListingPageExtractor extractor) : IAsyncDisposable
    {
        public VarusListingPageExtractor Extractor { get; } = extractor;

        public async ValueTask DisposeAsync()
        {
            httpClient.Dispose();
            await coordinator.DisposeAsync();
        }
    }
}
