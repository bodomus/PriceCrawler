using System.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace PriceCrawler.Infrastructure.Persistence;

/// <summary>Performs the explicit Development/Test schema initialization path.</summary>
public sealed class DatabaseSchemaInitializer(
    PriceCrawlerDbContext dbContext,
    SchemaBootstrapper schemaBootstrapper,
    ILogger<DatabaseSchemaInitializer> log)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        if (await IsPublicSchemaEmptyAsync(ct))
        {
            var baselinePath = SqlAssetLocator.ResolveBaselinePath();
            var baselineSql = await File.ReadAllTextAsync(baselinePath, ct);
            await using var command = connection.CreateCommand();
            command.CommandText = baselineSql;
            command.CommandTimeout = 120;
            await command.ExecuteNonQueryAsync(ct);
            await using var resetSearchPath = connection.CreateCommand();
            resetSearchPath.CommandText = "reset search_path;";
            await resetSearchPath.ExecuteNonQueryAsync(ct);
            log.LogInformation(
                "Initialized empty database from baseline {BaselineMigrationName}",
                DatabaseSchema.BaselineMigrationName);
            return;
        }

        await schemaBootstrapper.EnsureSchemaAsync(ct);
        log.LogInformation("Completed approved existing Development/Test database ensure path");
    }

    private async Task<bool> IsPublicSchemaEmptyAsync(CancellationToken ct)
    {
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              select not exists (
                                  select 1
                                  from pg_catalog.pg_class object
                                  join pg_catalog.pg_namespace namespace on namespace.oid = object.relnamespace
                                  where namespace.nspname = 'public'
                                    and object.relkind in ('r', 'p', 'v', 'm', 'S', 'f')
                              ) and not exists (
                                  select 1
                                  from pg_catalog.pg_proc routine
                                  join pg_catalog.pg_namespace namespace on namespace.oid = routine.pronamespace
                                  where namespace.nspname = 'public'
                              );
                              """;
        var value = await command.ExecuteScalarAsync(ct);
        return value is true || (value is not null && Convert.ToBoolean(value));
    }
}
