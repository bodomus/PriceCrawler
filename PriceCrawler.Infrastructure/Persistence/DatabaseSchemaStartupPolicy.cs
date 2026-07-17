namespace PriceCrawler.Infrastructure.Persistence;

/// <summary>Applies the non-bypassable environment safety rule before schema startup accesses the database.</summary>
public static class DatabaseSchemaStartupPolicy
{
    public static void EnsureSafe(string environmentName, DatabaseSchemaStartupMode configuredMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        if (configuredMode == DatabaseSchemaStartupMode.Ensure && !AllowsInitialization(environmentName))
        {
            throw new DatabaseSchemaStartupConfigurationException(
                environmentName,
                configuredMode,
                DatabaseSchemaStartupMode.ValidateOnly);
        }
    }

    public static DatabaseSchemaStartupMode GetDefaultMode(string environmentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        return AllowsInitialization(environmentName)
            ? DatabaseSchemaStartupMode.Ensure
            : DatabaseSchemaStartupMode.ValidateOnly;
    }

    public static bool AllowsInitialization(string environmentName)
        => environmentName.Equals("Development", StringComparison.OrdinalIgnoreCase)
           || environmentName.Equals("Test", StringComparison.OrdinalIgnoreCase);
}
