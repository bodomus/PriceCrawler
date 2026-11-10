using System.Net;
using System.Net.Sockets;

using PriceCrawler.Infrastructure.Crawler;

namespace PriceCrawler.Web.Tests;

public sealed class NetworkFailureClassifierTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void FirewallBlockedSocket_IsNetworkUnavailable()
    {
        // TinyWall on 09.11: HttpRequestException wrapping SocketException 10013 (access forbidden).
        var ex = new HttpRequestException(HttpRequestError.ConnectionError, "forbidden",
            new SocketException((int)SocketError.AccessDenied));

        Assert.True(NetworkFailureClassifier.IsNetworkUnavailable(ex));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NameResolutionFailure_IsNetworkUnavailable()
        => Assert.True(NetworkFailureClassifier.IsNetworkUnavailable(
            new HttpRequestException(HttpRequestError.NameResolutionError, "no such host")));

    [Fact]
    [Trait("Category", "Unit")]
    public void NestedSocketException_IsNetworkUnavailable()
        => Assert.True(NetworkFailureClassifier.IsNetworkUnavailable(
            new InvalidOperationException("wrapper", new IOException("io", new SocketException((int)SocketError.ConnectionRefused)))));

    [Fact]
    [Trait("Category", "Unit")]
    public void HttpStatusAndParsingFailures_AreNotNetworkUnavailable()
    {
        Assert.False(NetworkFailureClassifier.IsNetworkUnavailable(
            new HttpRequestException("server error", null, HttpStatusCode.BadGateway)));
        Assert.False(NetworkFailureClassifier.IsNetworkUnavailable(new FormatException("bad json")));
        Assert.False(NetworkFailureClassifier.IsNetworkUnavailable(new TimeoutException()));
    }
}
