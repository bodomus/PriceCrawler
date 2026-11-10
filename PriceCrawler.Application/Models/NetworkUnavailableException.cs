namespace PriceCrawler.Application.Models;

/// <summary>
/// Thrown when price collection stops early because the site has been unreachable for many items in a row
/// (MPC-108): a firewall blocks the collector, the connection is down or DNS fails.
/// </summary>
/// <param name="consecutiveFailures">Number of consecutive queue items that failed with <c>network_unavailable</c>.</param>
public sealed class NetworkUnavailableException(int consecutiveFailures)
    : Exception($"The site was unreachable for {consecutiveFailures} queue items in a row (network_unavailable). " +
                "Check the network and the firewall exception for the collector (docs/collector.md, TinyWall).")
{
    /// <summary>Number of consecutive queue items that failed with <c>network_unavailable</c>.</summary>
    public int ConsecutiveFailures { get; } = consecutiveFailures;
}
