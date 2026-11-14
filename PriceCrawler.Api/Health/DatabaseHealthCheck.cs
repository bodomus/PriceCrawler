using System.Data.Common;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using PriceCrawler.Infrastructure.Persistence;

namespace PriceCrawler.Api.Health;

/// <summary>Reports whether PostgreSQL answers a trivial query.</summary>
/// <param name="connectionFactory">Creates connections to the configured database.</param>
internal sealed class DatabaseHealthCheck(IPgConnectionFactory connectionFactory) : IHealthCheck
{
    /// <summary>Tag of checks that must pass before the API can serve requests.</summary>
    public const string ReadyTag = "ready";

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = (DbConnection)connectionFactory.Create();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = "select 1";
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (DbException exception)
        {
            return HealthCheckResult.Unhealthy("Database is unreachable.", exception);
        }
    }
}
