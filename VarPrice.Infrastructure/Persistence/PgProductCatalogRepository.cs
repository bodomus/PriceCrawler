using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;

using VarPrice.Domain.Interfaces;
using VarPrice.Domain.Models;

namespace VarPrice.Infrastructure.Persistence;

public sealed class PgProductCatalogRepository(PgRoutineExecutor routineExecutor) : IProductCatalogRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public async Task<ProductCatalogUpsertResult> UpsertDiscoveredAsync(
        IReadOnlyCollection<ProductCatalogUpsertItem> items,
        CancellationToken ct)
    {
        var prepared = ProductCatalogBatchPreparer.Prepare(items);
        if (prepared.Count == 0)
        {
            return new ProductCatalogUpsertResult(0, 0, 0);
        }

        var payload = JsonSerializer.Serialize(
            prepared.Select(x => new ProductCatalogJsonItem(
                x.Source,
                x.Url,
                x.NormalizedUrl,
                x.ExternalId,
                x.Slug,
                x.DiscoveredAtUtc.UtcDateTime)),
            JsonOptions);

        var result = await routineExecutor.QuerySingleOrDefaultAsync(
            DbRoutineCall.SetReturningFunction("product_catalog_upsert_discovered")
                .AddParameter("p_items", payload),
            reader => new ProductCatalogUpsertResult(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2)),
            ct);

        return result ?? new ProductCatalogUpsertResult(prepared.Count, 0, 0);
    }

    public async Task<ProductCatalogItem?> GetByIdAsync(long id, CancellationToken ct)
        => await routineExecutor.QuerySingleOrDefaultAsync(
            DbRoutineCall.SetReturningFunction("product_catalog_get_by_id")
                .AddParameter("p_id", id),
            MapItem,
            ct);

    public async Task<IReadOnlyList<ProductCatalogItem>> GetDueProductsAsync(
        int limit,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        string workerId,
        CancellationToken ct)
        => await routineExecutor.QueryAsync(
            DbRoutineCall.SetReturningFunction("product_catalog_get_due")
                .AddParameter("p_limit", Math.Max(1, limit))
                .AddParameter("p_now", nowUtc.UtcDateTime)
                .AddParameter("p_lease_seconds", Math.Max(30, (int)Math.Ceiling(leaseDuration.TotalSeconds)))
                .AddParameter("p_worker_id", workerId),
            MapItem,
            ct);

    public async Task MarkCheckedAsync(ProductCatalogCheckSuccess success, CancellationToken ct)
        => await routineExecutor.ExecuteAsync(
            DbRoutineCall.Procedure("product_catalog_mark_checked")
                .AddParameter("p_catalog_item_id", success.CatalogItemId)
                .AddParameter("p_checked_at", success.CheckedAtUtc.UtcDateTime)
                .AddParameter("p_next_check_at", success.NextCheckAtUtc.UtcDateTime)
                .AddParameter("p_external_id", success.ExternalId)
                .AddParameter("p_slug", success.Slug),
            ct);

    public async Task MarkFailedAsync(ProductCatalogCheckFailure failure, CancellationToken ct)
        => await routineExecutor.ExecuteAsync(
            DbRoutineCall.Procedure("product_catalog_mark_failed")
                .AddParameter("p_catalog_item_id", failure.CatalogItemId)
                .AddParameter("p_attempted_at", failure.AttemptedAtUtc.UtcDateTime)
                .AddParameter("p_next_check_at", failure.NextCheckAtUtc.UtcDateTime),
            ct);

    public async Task<ProductCatalogItem?> GetBySourceAndNormalizedUrlAsync(
        string source,
        string normalizedUrl,
        CancellationToken ct)
        => await routineExecutor.QuerySingleOrDefaultAsync(
            DbRoutineCall.SetReturningFunction("product_catalog_get_by_source_normalized_url")
                .AddParameter("p_source", source)
                .AddParameter("p_normalized_url", normalizedUrl),
            MapItem,
            ct);

    private static ProductCatalogItem MapItem(DbDataReader reader)
        => new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            ToDateTimeOffset(reader, 6),
            ToDateTimeOffset(reader, 7),
            reader.IsDBNull(8) ? null : ToDateTimeOffset(reader, 8),
            reader.IsDBNull(9) ? null : ToDateTimeOffset(reader, 9),
            reader.GetBoolean(10),
            reader.GetInt32(11));

    private static DateTimeOffset ToDateTimeOffset(DbDataReader reader, int ordinal)
    {
        var value = reader.GetFieldValue<DateTime>(ordinal);
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private sealed record ProductCatalogJsonItem(
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("normalized_url")]
        string NormalizedUrl,
        [property: JsonPropertyName("external_id")]
        string? ExternalId,
        [property: JsonPropertyName("slug")] string? Slug,
        [property: JsonPropertyName("discovered_at_utc")]
        DateTime DiscoveredAtUtc);
}
