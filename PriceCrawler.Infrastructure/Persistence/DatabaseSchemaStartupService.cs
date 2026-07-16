using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PriceCrawler.Infrastructure.Persistence;

/// <summary>Coordinates environment-safe initialization and schema compatibility validation.</summary>
public sealed class DatabaseSchemaStartupService(
    SchemaBootstrapper schemaBootstrapper,
    DatabaseSchemaVersionReader versionReader,
    IOptions<DatabaseSchemaOptions> options,
    ILogger<DatabaseSchemaStartupService> log)
{
    public async Task ValidateAndInitializeAsync(string environmentName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        var protectedEnvironment = IsProtectedEnvironment(environmentName);
        if (options.Value.AllowAutomaticInitialization && !protectedEnvironment)
        {
            log.LogInformation(
                "Automatic database initialization is enabled for {EnvironmentName}",
                environmentName);
            await schemaBootstrapper.EnsureSchemaAsync(ct);
        }
        else if (options.Value.AllowAutomaticInitialization)
        {
            log.LogWarning(
                "Automatic database initialization was requested for {EnvironmentName} but is disabled by policy",
                environmentName);
        }

        if (!options.Value.ValidateOnStartup && !protectedEnvironment)
        {
            log.LogWarning("Database schema startup validation is disabled for {EnvironmentName}", environmentName);
            return;
        }

        var result = await versionReader.ReadAsync(ct);
        if (!result.IsCompatible)
        {
            throw CreateMismatchException(environmentName, result, protectedEnvironment);
        }

        log.LogInformation(
            "Database schema version {SchemaVersion} is compatible in {EnvironmentName}",
            result.ActualVersion,
            environmentName);
    }

    private static bool IsProtectedEnvironment(string environmentName)
        => environmentName.Equals("Stage", StringComparison.OrdinalIgnoreCase)
           || environmentName.Equals("Staging", StringComparison.OrdinalIgnoreCase)
           || environmentName.Equals("Production", StringComparison.OrdinalIgnoreCase);

    private static DatabaseSchemaVersionMismatchException CreateMismatchException(
        string environmentName,
        DatabaseSchemaValidationResult result,
        bool protectedEnvironment)
    {
        string message;
        if (!result.MetadataTableExists)
        {
            message = $"""
                       Database schema metadata table 'schema_version' was not found.
                       Expected schema version: {result.ExpectedVersion}.
                       Environment: {environmentName}.
                       Run the database baseline/bootstrap process before starting the application.
                       """;
        }
        else if (result.ActualVersion is null)
        {
            message = $"""
                       Database schema metadata table 'schema_version' is empty.
                       Expected schema version: {result.ExpectedVersion}.
                       Environment: {environmentName}.
                       Run the database baseline/bootstrap process before starting the application.
                       """;
        }
        else if (result.ActualVersion < result.ExpectedVersion)
        {
            message = $"""
                       Database schema version mismatch.
                       Expected: {result.ExpectedVersion}.
                       Actual: {result.ActualVersion}.
                       Environment: {environmentName}.
                       Apply the required forward database migrations before starting the application.
                       """;
        }
        else
        {
            message = $"""
                       Database schema version mismatch.
                       Expected: {result.ExpectedVersion}.
                       Actual: {result.ActualVersion}.
                       Environment: {environmentName}.
                       The database is newer than this application release. Deploy a compatible application version.
                       """;
        }

        if (protectedEnvironment)
        {
            message += Environment.NewLine + "Automatic schema changes are disabled.";
        }

        return new DatabaseSchemaVersionMismatchException(message, result);
    }
}

