namespace PriceCrawler.Infrastructure.Persistence;

/// <summary>Validates schema metadata using only read-only SQL.</summary>
public sealed class DatabaseSchemaValidator(DatabaseSchemaVersionReader versionReader)
{
    public async Task<DatabaseSchemaValidationResult> ValidateAsync(
        string environmentName,
        DatabaseSchemaStartupMode startupMode,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        var result = await versionReader.ReadAsync(ct);
        if (result.IsCompatible)
        {
            return result;
        }

        throw CreateMismatchException(environmentName, startupMode, result);
    }

    private static DatabaseSchemaVersionMismatchException CreateMismatchException(
        string environmentName,
        DatabaseSchemaStartupMode startupMode,
        DatabaseSchemaValidationResult result)
    {
        var header = $"""
                     Database schema validation failed.
                     Environment: {environmentName}.
                     Startup mode: {startupMode}.
                     Expected schema version: {result.ExpectedVersion}.
                     """;
        string reason;
        string action;

        if (!result.MetadataTableExists)
        {
            reason = "Reason: schema_version table was not found.";
            action = "Apply the database baseline or required migrations before startup.";
        }
        else if (result.ActualVersion is null)
        {
            reason = "Reason: schema_version table is empty.";
            action = "Apply the database baseline or required migrations before startup.";
        }
        else if (result.ActualVersion < result.ExpectedVersion)
        {
            reason = $"Actual schema version: {result.ActualVersion}.";
            action = "Apply the required forward database migrations before startup.";
        }
        else
        {
            reason = $"Actual schema version: {result.ActualVersion}. The database is newer than this application release.";
            action = "Deploy a compatible application version.";
        }

        var mutationPolicy = startupMode == DatabaseSchemaStartupMode.ValidateOnly
            ? Environment.NewLine + "Automatic schema changes are disabled."
            : string.Empty;
        var message = header + Environment.NewLine + reason + mutationPolicy + Environment.NewLine + action;
        return new DatabaseSchemaVersionMismatchException(message, result);
    }
}
