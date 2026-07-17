using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PriceCrawler.Infrastructure.Persistence;

/// <summary>Coordinates the single configured schema startup mode for Web and Worker.</summary>
public sealed class DatabaseSchemaStartupCoordinator(
    DatabaseSchemaInitializer initializer,
    DatabaseSchemaValidator validator,
    IOptions<DatabaseSchemaOptions> options,
    ILogger<DatabaseSchemaStartupCoordinator> log)
{
    public async Task ExecuteAsync(string environmentName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        var startupMode = options.Value.StartupMode;

        try
        {
            DatabaseSchemaStartupPolicy.EnsureSafe(environmentName, startupMode);
            log.LogInformation(
                "Database schema startup beginning. Environment={Environment}; SchemaStartupMode={SchemaStartupMode}; ExpectedSchemaVersion={ExpectedSchemaVersion}",
                environmentName,
                startupMode,
                DatabaseSchema.ExpectedVersion);

            if (startupMode == DatabaseSchemaStartupMode.Ensure)
            {
                await initializer.EnsureAsync(ct);
            }

            var result = await validator.ValidateAsync(environmentName, startupMode, ct);
            log.LogInformation(
                "Database schema startup completed. Environment={Environment}; SchemaStartupMode={SchemaStartupMode}; ExpectedSchemaVersion={ExpectedSchemaVersion}; ActualSchemaVersion={ActualSchemaVersion}; Result={Result}",
                environmentName,
                startupMode,
                result.ExpectedVersion,
                result.ActualVersion,
                "Succeeded");
        }
        catch (DatabaseSchemaStartupConfigurationException exception)
        {
            log.LogError(
                exception,
                "Database schema startup failed. Environment={Environment}; SchemaStartupMode={SchemaStartupMode}; ExpectedSchemaVersion={ExpectedSchemaVersion}; ActualSchemaVersion={ActualSchemaVersion}; Result={Result}; Reason={Reason}",
                environmentName,
                startupMode,
                DatabaseSchema.ExpectedVersion,
                null,
                "Failed",
                "UnsafeConfiguration");
            throw;
        }
        catch (DatabaseSchemaVersionMismatchException exception)
        {
            log.LogError(
                exception,
                "Database schema startup failed. Environment={Environment}; SchemaStartupMode={SchemaStartupMode}; ExpectedSchemaVersion={ExpectedSchemaVersion}; ActualSchemaVersion={ActualSchemaVersion}; Result={Result}; Reason={Reason}",
                environmentName,
                startupMode,
                exception.ValidationResult.ExpectedVersion,
                exception.ValidationResult.ActualVersion,
                "Failed",
                GetValidationFailureReason(exception.ValidationResult));
            throw;
        }
        catch (Exception exception)
        {
            log.LogError(
                exception,
                "Database schema startup failed. Environment={Environment}; SchemaStartupMode={SchemaStartupMode}; ExpectedSchemaVersion={ExpectedSchemaVersion}; ActualSchemaVersion={ActualSchemaVersion}; Result={Result}; Reason={Reason}",
                environmentName,
                startupMode,
                DatabaseSchema.ExpectedVersion,
                null,
                "Failed",
                "InitializationOrValidationError");
            throw;
        }
    }

    private static string GetValidationFailureReason(DatabaseSchemaValidationResult result)
    {
        if (!result.MetadataTableExists) return "SchemaVersionTableMissing";
        if (result.ActualVersion is null) return "SchemaVersionMissing";
        return result.ActualVersion < result.ExpectedVersion
            ? "SchemaVersionOlder"
            : "SchemaVersionNewer";
    }
}
