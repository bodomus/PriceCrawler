using PriceCrawler.Infrastructure.Persistence;

namespace PriceCrawler.Api.Startup;

/// <summary>
/// Checks the database schema before the API accepts requests, with the mode from
/// <c>DatabaseSchema:StartupMode</c> (<c>ValidateOnly</c> by default: the API never changes the schema).
/// A wrong schema version stops the start.
/// </summary>
/// <param name="scopeFactory">Creates the scope the scoped schema services live in.</param>
/// <param name="configuration">Configuration with the connection string, for the log line.</param>
/// <param name="environment">Host environment; its name is part of the startup policy.</param>
/// <param name="logger">Startup logger.</param>
internal sealed partial class DatabaseSchemaStartupService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<DatabaseSchemaStartupService> logger) : IHostedService
{
    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string databaseTarget = DatabaseTarget.Describe(configuration);
        LogDatabaseTarget(logger, databaseTarget, environment.EnvironmentName);
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        DatabaseSchemaStartupCoordinator coordinator =
            scope.ServiceProvider.GetRequiredService<DatabaseSchemaStartupCoordinator>();
        await coordinator.ExecuteAsync(environment.EnvironmentName, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Logs which database the API uses (host and database name, no password).</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="databaseTarget">Description of the database from <see cref="DatabaseTarget"/>.</param>
    /// <param name="environmentName">Host environment name.</param>
    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Database target: {DatabaseTarget}; Environment={EnvironmentName}")]
    private static partial void LogDatabaseTarget(ILogger logger, string databaseTarget, string environmentName);
}
