using System.Net.Sockets;

namespace PriceCrawler.Infrastructure.Crawler;

/// <summary>
/// Recognises failures where a request never reached the site (MPC-108): the connection was refused
/// or blocked locally (a firewall such as TinyWall gives SocketException 10013 "access forbidden"),
/// or the host name could not be resolved. Such failures say nothing about the product page itself.
/// </summary>
internal static class NetworkFailureClassifier
{
    /// <summary>Checks whether an exception means the network or the site is unreachable.</summary>
    /// <param name="exception">Exception thrown by an HTTP request.</param>
    /// <returns><c>true</c> for connection and name-resolution failures, including nested ones.</returns>
    public static bool IsNetworkUnavailable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError })
            {
                return true;
            }

            if (current is SocketException)
            {
                return true;
            }
        }

        return false;
    }
}
