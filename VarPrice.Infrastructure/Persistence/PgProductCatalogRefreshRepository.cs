using System.Data.Common;

using VarPrice.Domain.Interfaces;
using VarPrice.Domain.Models;

namespace VarPrice.Infrastructure.Persistence;

public sealed class PgProductCatalogRefreshRepository(PgRoutineExecutor routineExecutor)
    : IProductCatalogRefreshRepository
{
    public async Task<long> StartAsync(
        string source,
        string discoverySource,
        DateTimeOffset startedAtUtc,
        CancellationToken ct)
        => await routineExecutor.ExecuteScalarAsync<long?>(
               DbRoutineCall.ScalarFunction("product_catalog_refresh_start")
                   .AddParameter("p_source", source)
                   .AddParameter("p_discovery_source", discoverySource)
                   .AddParameter("p_started_at", startedAtUtc.UtcDateTime),
               ct)
           ?? 0;

    public async Task CompleteAsync(
        long refreshId,
        ProductCatalogRefreshCompletion completion,
        CancellationToken ct)
        => await routineExecutor.ExecuteAsync(
            DbRoutineCall.Procedure("product_catalog_refresh_complete")
                .AddParameter("p_refresh_id", refreshId)
                .AddParameter("p_discovered_count", completion.DiscoveredCount)
                .AddParameter("p_accepted_count", completion.AcceptedCount)
                .AddParameter("p_inserted_count", completion.InsertedCount)
                .AddParameter("p_updated_count", completion.UpdatedCount)
                .AddParameter("p_deactivated_count", completion.DeactivatedCount)
                .AddParameter("p_reactivated_count", completion.ReactivatedCount)
                .AddParameter("p_finished_at", completion.FinishedAtUtc.UtcDateTime),
            ct);

    public async Task FailAsync(
        long refreshId,
        string status,
        string errorCode,
        string? errorMessage,
        DateTimeOffset finishedAtUtc,
        CancellationToken ct)
        => await routineExecutor.ExecuteAsync(
            DbRoutineCall.Procedure("product_catalog_refresh_fail")
                .AddParameter("p_refresh_id", refreshId)
                .AddParameter("p_status", status)
                .AddParameter("p_error_code", errorCode)
                .AddParameter("p_error_message", errorMessage)
                .AddParameter("p_finished_at", finishedAtUtc.UtcDateTime),
            ct);

    public async Task<ProductCatalogRefreshSession?> GetByIdAsync(long refreshId, CancellationToken ct)
        => await routineExecutor.QuerySingleOrDefaultAsync(
            DbRoutineCall.SetReturningFunction("product_catalog_refresh_get_by_id")
                .AddParameter("p_refresh_id", refreshId),
            MapSession,
            ct);

    private static ProductCatalogRefreshSession MapSession(DbDataReader reader)
        => new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            ToDateTimeOffset(reader, 3),
            reader.IsDBNull(4) ? null : ToDateTimeOffset(reader, 4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.GetInt32(10),
            reader.GetInt32(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13));

    private static DateTimeOffset ToDateTimeOffset(DbDataReader reader, int ordinal)
    {
        var value = reader.GetFieldValue<DateTime>(ordinal);
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
