using System.Data;
using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace VarPrice.Infrastructure.Persistence;

public sealed class SchemaBootstrapper(
    VarPriceDbContext dbContext,
    StageSafetyGuard stageSafetyGuard,
    ILogger<SchemaBootstrapper> log)
{
    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        stageSafetyGuard.EnsureSchemaBootstrapAllowed();

        const int attempts = 30;
        const int delayMs = 1000;
        Exception? last = null;

        for (var i = 1; i <= attempts; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var connection = dbContext.Database.GetDbConnection();
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync(ct);
                }

                await using var transaction = await connection.BeginTransactionAsync(ct);
                await MigrateLegacySchemaAsync(connection, transaction, ct);
                await ExecuteSchemaScriptAsync(connection, transaction, ct);
                await ApplyRoutineScriptsAsync(connection, transaction, ct);
                await MigrateLegacyDataAsync(connection, transaction, ct);
                await DropLegacyTablesAsync(connection, transaction, ct);
                await transaction.CommitAsync(ct);

                log.LogInformation("Schema ensured");
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                log.LogWarning(ex, "Postgres not ready (attempt {Attempt}/{Attempts})", i, attempts);
                await Task.Delay(delayMs, ct);
            }
        }

        throw new InvalidOperationException("Failed to ensure schema (Postgres not ready)", last);
    }

    private static async Task MigrateLegacySchemaAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken ct)
    {
        if (await TableHasColumnAsync(connection, transaction, "crawler_run", "run_id", ct))
        {
            await RenameTableAsync(connection, transaction, "crawler_run", "crawler_run_legacy", ct);
        }

        if (await TableExistsAsync(connection, transaction, "ingestion_run", ct)
            && await TableExistsAsync(connection, transaction, "crawler_run_legacy", ct)
            && !await TableExistsAsync(connection, transaction, "ingestion_run_legacy", ct))
        {
            await RenameTableAsync(connection, transaction, "ingestion_run", "ingestion_run_legacy", ct);
        }

        if (await TableHasColumnAsync(connection, transaction, "price_collect_queue", "queue_id", ct))
        {
            await RenameTableAsync(connection, transaction, "price_collect_queue", "price_collect_queue_legacy", ct);
        }

        if (await TableHasColumnAsync(connection, transaction, "product", "product_key", ct))
        {
            await RenameTableAsync(connection, transaction, "product", "product_legacy", ct);
        }

        if (await TableHasColumnAsync(connection, transaction, "price_snapshot", "snapshot_id", ct))
        {
            await RenameTableAsync(connection, transaction, "price_snapshot", "price_snapshot_legacy", ct);
        }

        if (await TableExistsAsync(connection, transaction, "product_errors", ct))
        {
            await RenameTableAsync(connection, transaction, "product_errors", "product_errors_legacy", ct);
        }
    }

    private static async Task ExecuteSchemaScriptAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken ct)
    {
        var schemaPath = SqlAssetLocator.ResolveSchemaPath();
        var sql = await File.ReadAllTextAsync(schemaPath, ct);
        await ExecuteNonQueryAsync(connection, transaction, sql, ct);
    }

    private async Task ApplyRoutineScriptsAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken ct)
    {
        var scripts = await DbRoutineScriptCatalog.LoadAsync(ct);
        foreach (var script in scripts)
        {
            var appliedHash = await GetAppliedRoutineScriptHashAsync(connection, transaction, script.Name, ct);
            if (string.Equals(appliedHash, script.Hash, StringComparison.Ordinal))
            {
                continue;
            }

            await ExecuteNonQueryAsync(connection, transaction, script.Sql, ct);
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                insert into db_routine_script(script_name, script_hash, applied_at)
                values(@script_name, @script_hash, now())
                on conflict (script_name) do update
                set script_hash = excluded.script_hash,
                    applied_at = excluded.applied_at;
                """,
                [("@script_name", script.Name), ("@script_hash", script.Hash)],
                ct);

            log.LogInformation(
                appliedHash is null
                    ? "Applied routine script {ScriptName}"
                    : "Reapplied routine script {ScriptName}",
                script.Name);
        }
    }

    private static async Task MigrateLegacyDataAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken ct)
    {
        if (!await TableExistsAsync(connection, transaction, "crawler_run_legacy", ct)
            && !await TableExistsAsync(connection, transaction, "product_legacy", ct)
            && !await TableExistsAsync(connection, transaction, "price_snapshot_legacy", ct)
            && !await TableExistsAsync(connection, transaction, "price_collect_queue_legacy", ct)
            && !await TableExistsAsync(connection, transaction, "product_errors_legacy", ct))
        {
            return;
        }

        if (await TableExistsAsync(connection, transaction, "crawler_run_legacy", ct))
        {
            await ExecuteNonQueryAsync(connection, transaction, """
                                                                insert into crawler_run(id, started_at, finished_at, status, source, note)
                                                                select
                                                                    run_id,
                                                                    started_at,
                                                                    finished_at,
                                                                    case status::text
                                                                        when '0' then 'running'
                                                                        when '1' then 'ok'
                                                                        when '2' then 'error'
                                                                        when 'running' then 'running'
                                                                        when 'ok' then 'ok'
                                                                        else 'error'
                                                                    end,
                                                                    source,
                                                                    note
                                                                from crawler_run_legacy
                                                                on conflict (id) do nothing;
                                                                """, ct);
        }

        if (await TableExistsAsync(connection, transaction, "ingestion_run_legacy", ct))
        {
            await ExecuteNonQueryAsync(connection, transaction, """
                                                                insert into ingestion_run(
                                                                    ingestion_run_id,
                                                                    crawler_run_id,
                                                                    started_at,
                                                                    finished_at,
                                                                    status,
                                                                    error_code,
                                                                    error_message)
                                                                select
                                                                    ingestion_run_id,
                                                                    crawler_run_id,
                                                                    started_at,
                                                                    finished_at,
                                                                    status,
                                                                    error_code,
                                                                    error_message
                                                                from ingestion_run_legacy
                                                                on conflict (ingestion_run_id) do nothing;
                                                                """, ct);
        }

        if (await TableExistsAsync(connection, transaction, "price_collect_queue_legacy", ct))
        {
            await ExecuteNonQueryAsync(connection, transaction, """
                                                                insert into price_collect_queue(
                                                                    id,
                                                                    run_id,
                                                                    url,
                                                                    status,
                                                                    attempt,
                                                                    max_attempts,
                                                                    next_attempt_at,
                                                                    reserved_at,
                                                                    lease_until,
                                                                    reserved_by,
                                                                    idempotency_key,
                                                                    last_error_code,
                                                                    last_http_status,
                                                                    last_error_message,
                                                                    created_at,
                                                                    updated_at,
                                                                    finished_at)
                                                                select
                                                                    queue_id,
                                                                    run_id,
                                                                    url,
                                                                    coalesce(status, 'pending'),
                                                                    coalesce(attempt, 0),
                                                                    coalesce(max_attempts, 0),
                                                                    next_attempt_at,
                                                                    reserved_at,
                                                                    lease_until,
                                                                    reserved_by,
                                                                    idempotency_key,
                                                                    last_error_code,
                                                                    last_http_status,
                                                                    last_error_message,
                                                                    coalesce(created_at, now()),
                                                                    updated_at,
                                                                    finished_at
                                                                from price_collect_queue_legacy
                                                                on conflict (id) do nothing;
                                                                """, ct);
        }

        if (await TableExistsAsync(connection, transaction, "product_legacy", ct))
        {
            await ExecuteNonQueryAsync(connection, transaction, """
                                                                create temporary table if not exists product_migration_map(
                                                                    old_product_key bigint primary key,
                                                                    new_product_id bigint not null
                                                                ) on commit drop;

                                                                insert into product(
                                                                    external_id,
                                                                    name,
                                                                    url,
                                                                    slug,
                                                                    pack_value,
                                                                    pack_unit,
                                                                    created_at,
                                                                    updated_at)
                                                                select distinct on (coalesce(nullif(trim(url), ''), concat('legacy://product/', product_key)))
                                                                    nullif(trim(product_id), ''),
                                                                    coalesce(nullif(trim(name), ''), nullif(trim(product_id), ''), concat('product-', product_key)),
                                                                    coalesce(nullif(trim(url), ''), concat('legacy://product/', product_key)),
                                                                    nullif(regexp_replace(trim(both '/' from split_part(coalesce(nullif(trim(url), ''), ''), '?', 1)), '^.*/', ''), ''),
                                                                    pack_value,
                                                                    pack_unit,
                                                                    coalesce(created_at, now()),
                                                                    coalesce(last_seen_at, created_at)
                                                                from product_legacy
                                                                order by coalesce(nullif(trim(url), ''), concat('legacy://product/', product_key)),
                                                                    last_seen_at desc nulls last,
                                                                    product_key desc
                                                                on conflict (url) do update
                                                                set external_id = coalesce(excluded.external_id, product.external_id),
                                                                    name = excluded.name,
                                                                    slug = coalesce(excluded.slug, product.slug),
                                                                    pack_value = excluded.pack_value,
                                                                    pack_unit = excluded.pack_unit,
                                                                    updated_at = excluded.updated_at;

                                                                insert into product_migration_map(old_product_key, new_product_id)
                                                                select
                                                                    legacy.product_key,
                                                                    current_product.id
                                                                from product_legacy legacy
                                                                join product current_product
                                                                    on current_product.url = coalesce(nullif(trim(legacy.url), ''), concat('legacy://product/', legacy.product_key))
                                                                on conflict (old_product_key) do nothing;
                                                                """, ct);
        }

        if (await TableExistsAsync(connection, transaction, "price_snapshot_legacy", ct))
        {
            await ExecuteNonQueryAsync(connection, transaction, """
                                                                insert into price_snapshot(
                                                                    id,
                                                                    run_id,
                                                                    product_id,
                                                                    captured_at,
                                                                    price,
                                                                    old_price,
                                                                    promo_flag,
                                                                    in_stock,
                                                                    queue_id)
                                                                select
                                                                    snapshot_id,
                                                                    run_id,
                                                                    mapping.new_product_id,
                                                                    coalesce(captured_at, now()),
                                                                    coalesce(final_price, regular_price),
                                                                    regular_price,
                                                                    coalesce(promo_flag, false),
                                                                    coalesce(in_stock, false),
                                                                    queue_id
                                                                from price_snapshot_legacy snapshot
                                                                join product_migration_map mapping on mapping.old_product_key = snapshot.product_key
                                                                on conflict (id) do nothing;
                                                                """, ct);
        }

        if (await TableExistsAsync(connection, transaction, "product_errors_legacy", ct))
        {
            await ExecuteNonQueryAsync(connection, transaction, """
                                                                insert into crawl_error(
                                                                    run_id,
                                                                    queue_id,
                                                                    product_id,
                                                                    url,
                                                                    error_code,
                                                                    http_status,
                                                                    error_message,
                                                                    created_at)
                                                                select distinct
                                                                    legacy_error.run_id,
                                                                    legacy_error.queue_id,
                                                                    mapping.new_product_id,
                                                                    coalesce(product.url, snapshot_product.url),
                                                                    nullif(trim(legacy_error.error_code), ''),
                                                                    null,
                                                                    nullif(trim(legacy_error.error_message), ''),
                                                                    coalesce(legacy_error.occurred_at, now())
                                                                from product_errors_legacy legacy_error
                                                                left join product_migration_map mapping on mapping.old_product_key = legacy_error.product_key
                                                                left join product on product.id = mapping.new_product_id
                                                                left join (
                                                                    select
                                                                        snapshot.snapshot_id,
                                                                        product_current.url
                                                                    from price_snapshot_legacy snapshot
                                                                    join product_migration_map mapping_snapshot on mapping_snapshot.old_product_key = snapshot.product_key
                                                                    join product product_current on product_current.id = mapping_snapshot.new_product_id
                                                                ) snapshot_product on snapshot_product.snapshot_id = legacy_error.price_snapshot_id;
                                                                """, ct);
        }
    }

    private static async Task DropLegacyTablesAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken ct)
    {
        await ExecuteNonQueryAsync(connection, transaction, """
                                                            drop table if exists product_errors_legacy cascade;
                                                            drop table if exists price_snapshot_legacy cascade;
                                                            drop table if exists product_legacy cascade;
                                                            drop table if exists price_collect_queue_legacy cascade;
                                                            drop table if exists ingestion_run_legacy cascade;
                                                            drop table if exists crawler_run_legacy cascade;
                                                            """, ct);
    }

    private static async Task<bool> TableExistsAsync(
        DbConnection connection,
        DbTransaction transaction,
        string tableName,
        CancellationToken ct)
        => await ScalarBoolAsync(connection, transaction, """
                                                          select exists (
                                                              select 1
                                                              from information_schema.tables
                                                              where table_schema = current_schema()
                                                                and table_name = @table_name
                                                          );
                                                          """, ("@table_name", tableName), ct);

    private static async Task<bool> TableHasColumnAsync(
        DbConnection connection,
        DbTransaction transaction,
        string tableName,
        string columnName,
        CancellationToken ct)
        => await ScalarBoolAsync(connection, transaction, """
                                                          select exists (
                                                              select 1
                                                              from information_schema.columns
                                                              where table_schema = current_schema()
                                                                and table_name = @table_name
                                                                and column_name = @column_name
                                                          );
                                                          """, ("@table_name", tableName), ("@column_name", columnName),
            ct);

    private static async Task RenameTableAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sourceName,
        string targetName,
        CancellationToken ct)
    {
        if (await TableExistsAsync(connection, transaction, targetName, ct))
        {
            return;
        }

        await ExecuteNonQueryAsync(connection, transaction, $"alter table {sourceName} rename to {targetName};", ct);
    }

    private static async Task<bool> ScalarBoolAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        (string Name, object? Value) parameter,
        CancellationToken ct)
        => await ScalarBoolAsync(connection, transaction, sql, [parameter], ct);

    private static async Task<bool> ScalarBoolAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        (string Name, object? Value) parameterOne,
        (string Name, object? Value) parameterTwo,
        CancellationToken ct)
        => await ScalarBoolAsync(connection, transaction, sql, [parameterOne, parameterTwo], ct);

    private static async Task<bool> ScalarBoolAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        IReadOnlyList<(string Name, object? Value)> parameters,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            AddParameter(command, parameter.Name, parameter.Value);
        }

        var scalar = await command.ExecuteScalarAsync(ct);
        return scalar is true || (scalar is not null && Convert.ToBoolean(scalar));
    }

    private static async Task<string?> GetAppliedRoutineScriptHashAsync(
        DbConnection connection,
        DbTransaction transaction,
        string scriptName,
        CancellationToken ct)
    {
        await using var command = CreateCommand(
            connection,
            transaction,
            """
            select script_hash
            from db_routine_script
            where script_name = @script_name;
            """,
            [("@script_name", scriptName)]);
        var scalar = await command.ExecuteScalarAsync(ct);
        return scalar is null or DBNull ? null : Convert.ToString(scalar);
    }

    private static async Task ExecuteNonQueryAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task ExecuteNonQueryAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        IReadOnlyList<(string Name, object? Value)> parameters,
        CancellationToken ct)
    {
        await using var command = CreateCommand(connection, transaction, sql, parameters);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static DbCommand CreateCommand(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        IReadOnlyList<(string Name, object? Value)> parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            AddParameter(command, parameter.Name, parameter.Value);
        }

        return command;
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
