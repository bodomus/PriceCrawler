using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace PriceCrawler.Api.Health;

/// <summary>Maps the health endpoints.</summary>
internal static class HealthEndpoints
{
    /// <summary>
    /// Maps <c>/health</c> (the process is up, no dependencies are checked) and <c>/health/ready</c>
    /// (the database answers).
    /// </summary>
    /// <param name="endpoints">Application route builder.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapApiHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health", new HealthCheckOptions { Predicate = static _ => false });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = static registration => registration.Tags.Contains(DatabaseHealthCheck.ReadyTag),
        });
        return endpoints;
    }
}
