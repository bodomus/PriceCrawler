namespace PriceCrawler.Infrastructure.Persistence;

/// <summary>Thrown before database access when a schema startup mode is unsafe for the environment.</summary>
public sealed class DatabaseSchemaStartupConfigurationException : InvalidOperationException
{
    public DatabaseSchemaStartupConfigurationException(
        string environmentName,
        DatabaseSchemaStartupMode configuredMode,
        DatabaseSchemaStartupMode requiredMode)
        : base($"""
               Unsafe database schema startup configuration.
               Environment: {environmentName}.
               Configured mode: {configuredMode}.
               Required mode: {requiredMode}.
               Startup aborted before database schema mutation.
               """)
    {
        EnvironmentName = environmentName;
        ConfiguredMode = configuredMode;
        RequiredMode = requiredMode;
    }

    public string EnvironmentName { get; }

    public DatabaseSchemaStartupMode ConfiguredMode { get; }

    public DatabaseSchemaStartupMode RequiredMode { get; }
}
