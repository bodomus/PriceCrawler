using System.Data;
using System.Data.Common;

using Microsoft.EntityFrameworkCore;

namespace PriceCrawler.Infrastructure.Persistence;

/// <summary>Reads PriceCrawler schema metadata without changing the database.</summary>
public sealed class DatabaseSchemaVersionReader(PriceCrawlerDbContext dbContext)
{
    public async Task<DatabaseSchemaValidationResult> ReadAsync(CancellationToken ct = default)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        if (!await MetadataTableExistsAsync(connection, ct))
        {
            return new DatabaseSchemaValidationResult(false, null, DatabaseSchema.ExpectedVersion);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "select max(version) from public.schema_version;";
        var scalar = await command.ExecuteScalarAsync(ct);
        var actualVersion = scalar is null or DBNull ? (int?)null : Convert.ToInt32(scalar);
        return new DatabaseSchemaValidationResult(true, actualVersion, DatabaseSchema.ExpectedVersion);
    }

    private static async Task<bool> MetadataTableExistsAsync(DbConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "select to_regclass('public.schema_version') is not null;";
        var scalar = await command.ExecuteScalarAsync(ct);
        return scalar is true || (scalar is not null && Convert.ToBoolean(scalar));
    }
}
