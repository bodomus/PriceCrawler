using System.Diagnostics;
using System.Net;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using VarPrice.Application.Models;
using VarPrice.Infrastructure.Crawler;

namespace VarPrice.Web.Tests;

public sealed class VarusProductCardExtractorResiliencyTests
{
    [Fact]
    public async Task ExtractAsync_When200AndParsable_ReturnsCard()
    {
        await using var harness = CreateHarness(
            new SequenceHttpMessageHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, ValidProductHtml))));

        var result = await harness.Extractor.ExtractAsync("https://varus.ua/p/1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Card);
        Assert.Equal(200, result.HttpStatus);
        Assert.Equal(1, harness.Handler.CallCount);
    }

    [Fact]
    public async Task ExtractAsync_When404_ReturnsNotFoundWithoutRetry()
    {
        await using var harness = CreateHarness(
            new SequenceHttpMessageHandler((_, _) =>
                Task.FromResult(Response(HttpStatusCode.NotFound, "<html></html>"))),
            retryCount: 2);

        var result = await harness.Extractor.ExtractAsync("https://varus.ua/p/missing", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(CrawlerErrorCodes.NotFound, result.ErrorCode);
        Assert.Equal(404, result.HttpStatus);
        Assert.Equal(1, harness.Handler.CallCount);
    }

    [Fact]
    public async Task ExtractAsync_When429Then200_RetriesAndSucceeds()
    {
        await using var harness = CreateHarness(
            new SequenceHttpMessageHandler(
                (_, _) => Task.FromResult(Response(HttpStatusCode.TooManyRequests, "retry")),
                (_, _) => Task.FromResult(Response(HttpStatusCode.OK, ValidProductHtml))),
            retryCount: 2,
            retryBaseDelayMs: 1);

        var result = await harness.Extractor.ExtractAsync("https://varus.ua/p/limited", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, harness.Handler.CallCount);
    }

    [Fact]
    public async Task ExtractAsync_When500Exhausted_ReturnsHttp5xx()
    {
        await using var harness = CreateHarness(
            new SequenceHttpMessageHandler(
                (_, _) => Task.FromResult(Response(HttpStatusCode.InternalServerError, "e1")),
                (_, _) => Task.FromResult(Response(HttpStatusCode.InternalServerError, "e2")),
                (_, _) => Task.FromResult(Response(HttpStatusCode.InternalServerError, "e3"))),
            retryCount: 2,
            retryBaseDelayMs: 1);

        var result = await harness.Extractor.ExtractAsync("https://varus.ua/p/500", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(CrawlerErrorCodes.Http5xx, result.ErrorCode);
        Assert.Equal(3, harness.Handler.CallCount);
    }

    [Fact]
    public async Task ExtractAsync_WhenTimeout_RetriesAndReturnsTimeout()
    {
        await using var harness = CreateHarness(
            new SequenceHttpMessageHandler(
                async (_, ct) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    return Response(HttpStatusCode.OK, ValidProductHtml);
                },
                async (_, ct) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    return Response(HttpStatusCode.OK, ValidProductHtml);
                }),
            retryCount: 1,
            requestTimeoutSeconds: 1,
            retryBaseDelayMs: 1);

        var result = await harness.Extractor.ExtractAsync("https://varus.ua/p/timeout", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(CrawlerErrorCodes.Timeout, result.ErrorCode);
        Assert.Equal(2, harness.Handler.CallCount);
    }

    [Fact]
    public async Task ExtractAsync_WhenHtmlCannotBeParsed_ReturnsParseFailedWithoutRetry()
    {
        await using var harness = CreateHarness(
            new SequenceHttpMessageHandler((_, _) =>
                Task.FromResult(Response(HttpStatusCode.OK, "<html><body>No sku and no price</body></html>"))),
            retryCount: 2);

        var result = await harness.Extractor.ExtractAsync("https://varus.ua/p/parse", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(CrawlerErrorCodes.ParseFailed, result.ErrorCode);
        Assert.Equal(1, harness.Handler.CallCount);
    }

    [Fact]
    public async Task ExtractAsync_WhenBreakerOpened_WaitsBeforeNextRequest()
    {
        await using var harness = CreateHarness(
            new SequenceHttpMessageHandler(
                (_, _) => Task.FromResult(Response(HttpStatusCode.InternalServerError, "first")),
                (_, _) => Task.FromResult(Response(HttpStatusCode.InternalServerError, "second")),
                (_, _) => Task.FromResult(Response(HttpStatusCode.OK, ValidProductHtml))),
            retryCount: 0,
            breakerFailureThreshold: 2,
            breakerOpenSeconds: 1);

        var first = await harness.Extractor.ExtractAsync("https://varus.ua/p/a", CancellationToken.None);
        var second = await harness.Extractor.ExtractAsync("https://varus.ua/p/b", CancellationToken.None);

        var sw = Stopwatch.StartNew();
        var third = await harness.Extractor.ExtractAsync("https://varus.ua/p/c", CancellationToken.None);
        sw.Stop();

        Assert.False(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.True(third.IsSuccess);
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(900),
            $"Expected breaker delay >= 900ms, actual={sw.Elapsed.TotalMilliseconds}ms");
        Assert.Equal(3, harness.Handler.CallCount);
    }

    private static ExtractorHarness CreateHarness(
        SequenceHttpMessageHandler handler,
        int retryCount = 2,
        int retryBaseDelayMs = 1,
        int requestTimeoutSeconds = 2,
        int breakerFailureThreshold = 20,
        int breakerOpenSeconds = 60)
    {
        var crawlerOptions = Options.Create(new CrawlerOptions
        {
            MaxConcurrency = 4,
            RequestsPerSecond = 100d,
            RequestTimeoutSeconds = requestTimeoutSeconds,
            RetryCount = retryCount,
            RetryBaseDelayMs = retryBaseDelayMs,
            JitterDelayMsMin = 0,
            JitterDelayMsMax = 0,
            BreakerFailureThreshold = breakerFailureThreshold,
            BreakerOpenSeconds = breakerOpenSeconds
        });

        var coordinator = new VarusRequestCoordinator(crawlerOptions, NullLogger<VarusRequestCoordinator>.Instance);
        var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var factory = new StubHttpClientFactory(httpClient);
        var extractor = new VarusProductCardExtractor(
            factory,
            coordinator,
            crawlerOptions,
            NullLogger<VarusProductCardExtractor>.Instance);

        return new ExtractorHarness(handler, httpClient, coordinator, extractor);
    }

    private static HttpResponseMessage Response(HttpStatusCode statusCode, string content)
        => new(statusCode)
        {
            Content = new StringContent(content)
        };

    private const string ValidProductHtml =
        """
        <html>
          <head>
            <title>Carrot</title>
            <script type="application/ld+json">
              {"@type":"Product","name":"Carrot","sku":"12345","offers":{"price":"49.90","priceCurrency":"UAH"}}
            </script>
          </head>
          <body>Carrot page</body>
        </html>
        """;

    private sealed class StubHttpClientFactory(HttpClient httpClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => httpClient;
    }

    private sealed class SequenceHttpMessageHandler(
        params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] responses) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses =
            new(responses);

        private readonly object _sync = new();
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> step;
            lock (_sync)
            {
                if (_responses.Count == 0)
                {
                    throw new InvalidOperationException("No more responses configured for SequenceHttpMessageHandler.");
                }

                step = _responses.Dequeue();
            }

            return step(request, cancellationToken);
        }
    }

    private sealed class ExtractorHarness(
        SequenceHttpMessageHandler handler,
        HttpClient httpClient,
        VarusRequestCoordinator coordinator,
        VarusProductCardExtractor extractor) : IAsyncDisposable
    {
        public SequenceHttpMessageHandler Handler { get; } = handler;
        public VarusProductCardExtractor Extractor { get; } = extractor;

        public async ValueTask DisposeAsync()
        {
            httpClient.Dispose();
            await coordinator.DisposeAsync();
        }
    }
}
