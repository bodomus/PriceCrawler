using Microsoft.Extensions.Configuration;

using Npgsql;

namespace VarPrice.Infrastructure.Persistence;

public sealed class TargetDatabaseResolver(IConfiguration configuration) : ITargetDatabaseResolver
{
    public const string TargetConfigKey = "Database:Target";

    public SelectedDatabase Resolve()
    {
        var targetValue = configuration[TargetConfigKey];
        if (string.IsNullOrWhiteSpace(targetValue))
        {
            throw new InvalidOperationException(
                $"Database target is not configured. Set '{TargetConfigKey}' to 'Dev' or 'Stage'.");
        }

        if (!Enum.TryParse<DatabaseTarget>(targetValue, ignoreCase: true, out var target))
        {
            throw new InvalidOperationException(
                $"Invalid database target '{targetValue}'. Allowed values: Dev, Stage.");
        }

        var connectionStringName = GetConnectionStringName(target);
        var connectionString = configuration.GetConnectionString(connectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{connectionStringName}' is not configured for database target '{target}'.");
        }

        var databaseName = GetDatabaseName(connectionStringName, connectionString);
        var expectedDatabaseName = GetExpectedDatabaseName(target);
        if (!string.Equals(databaseName, expectedDatabaseName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Database target '{target}' must use database '{expectedDatabaseName}', but connection string '{connectionStringName}' uses '{databaseName}'.");
        }

        return new SelectedDatabase(target, connectionString, databaseName);
    }

    private static string GetConnectionStringName(DatabaseTarget target)
        => target switch
        {
            DatabaseTarget.Dev => "PostgresDev",
            DatabaseTarget.Stage => "PostgresStage",
            _ => throw new InvalidOperationException($"Unsupported database target '{target}'.")
        };

    private static string GetExpectedDatabaseName(DatabaseTarget target)
        => target switch
        {
            DatabaseTarget.Dev => "varprice",
            DatabaseTarget.Stage => "varprice_stage",
            _ => throw new InvalidOperationException($"Unsupported database target '{target}'.")
        };

    private static string GetDatabaseName(string connectionStringName, string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(builder.Database))
            {
                throw new InvalidOperationException(
                    $"Connection string '{connectionStringName}' must include a database name.");
            }

            return builder.Database;
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"Connection string '{connectionStringName}' is invalid: {ex.Message}",
                ex);
        }
    }
}
