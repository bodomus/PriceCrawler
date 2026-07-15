using System.Net;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PriceCrawler.Application.Models;
using PriceCrawler.Domain.Enums;
using PriceCrawler.Infrastructure.Crawler;

namespace PriceCrawler.Web.Tests;

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
            ProductListingHtml(
                "/product-a?utm=1#card",
                "https://varus.ua/product-b",
                "https://example.com/not-varus"));

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
        await using var harness = CreateHarness(ProductListingHtml("/single-product"));

        var result = await harness.Extractor.ExtractAsync(
            "https://varus.ua/kovbasi~brand_gremio-de-la-carne",
            CancellationToken.None);

        Assert.Null(result.Issue);
        Assert.Equal(["https://varus.ua/single-product"], result.ProductUrls);
    }

    [Fact]
    public async Task ExtractAsync_DeduplicatesProductUrls()
    {
        await using var harness = CreateHarness(ProductListingHtml(
            "/same-product?tracking=1",
            "https://varus.ua/same-product#duplicate"));

        var result = await harness.Extractor.ExtractAsync(
            "https://varus.ua/kovbasi~brand_espana",
            CancellationToken.None);

        Assert.Null(result.Issue);
        Assert.Equal(["https://varus.ua/same-product"], result.ProductUrls);
    }

    [Fact]
    public async Task ExtractAsync_WhenNoProducts_ReturnsListingNoProductsFound()
    {
        await using var harness = CreateHarness(
            """
            <html><body>
              <a href="/buyers">Buyers</a><a href="/promotion">Promotion</a>
              <a href="/loyalty">Loyalty</a><a href="/help">Help</a>
              <a href="/giftcards">Gift cards</a><a href="/stores">Stores</a>
              <a href="/own-tm">Own TM</a><a href="/work">Work</a><a href="/ordering">Ordering</a>
            </body></html>
            """);

        var result = await harness.Extractor.ExtractAsync(
            "https://varus.ua/kovbasi~brand_espana",
            CancellationToken.None);

        Assert.Equal(CrawlerErrorCodes.ListingNoProductsFound, result.ErrorCode);
        Assert.False(result.IsTransient);
        Assert.Empty(result.ProductUrls);
    }

    [Fact]
    public async Task ExtractAsync_RejectsListingInvalidSchemesAndNonProductJsonLd()
    {
        await using var harness = CreateHarness(ProductListingHtml(
            "https://varus.ua/kovbasi~brand_espana#self",
            "http://varus.ua/http-product",
            "javascript:alert(1)",
            "https://example.com/external"));

        var result = await harness.Extractor.ExtractAsync(
            "https://varus.ua/kovbasi~brand_espana",
            CancellationToken.None);

        Assert.Equal(CrawlerErrorCodes.ListingNoProductsFound, result.ErrorCode);
        Assert.Empty(result.ProductUrls);
    }

    private static string ProductListingHtml(params string[] urls)
    {
        var items = string.Join(",", urls.Select((url, index) =>
            $$"""
              {
                "@type": "ListItem",
                "position": {{index + 1}},
                "item": { "@type": "Product", "sku": "sku-{{index + 1}}", "url": {{System.Text.Json.JsonSerializer.Serialize(url)}} }
              }
              """));

        return $$"""
          <html><body>
            <script type="application/ld+json">
              { "@context": "https://schema.org", "@type": "ItemList", "itemListElement": [{{items}}] }
            </script>
          </body></html>
          """;
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
