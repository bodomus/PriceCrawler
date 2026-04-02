using System.Data.Common;
using System.Globalization;

namespace VarPrice.Infrastructure.Persistence;

public sealed class PgRoutineExecutor(IPgConnectionFactory factory)
{
    public async Task<T?> ExecuteScalarAsync<T>(DbRoutineCall call, CancellationToken ct)
    {
        await using var connection = (DbConnection)factory.Create();
        await connection.OpenAsync(ct);
        return await ExecuteScalarAsync<T>(connection, transaction: null, call, ct);
    }

    public async Task<T?> ExecuteScalarAsync<T>(
        DbConnection connection,
        DbTransaction? transaction,
        DbRoutineCall call,
        CancellationToken ct)
    {
        await using var command = CreateCommand(connection, transaction, call);
        var scalar = await command.ExecuteScalarAsync(ct);
        return ConvertScalar<T>(scalar);
    }

    public async Task ExecuteAsync(DbRoutineCall call, CancellationToken ct)
    {
        await using var connection = (DbConnection)factory.Create();
        await connection.OpenAsync(ct);
        await ExecuteAsync(connection, transaction: null, call, ct);
    }

    public async Task ExecuteAsync(
        DbConnection connection,
        DbTransaction? transaction,
        DbRoutineCall call,
        CancellationToken ct)
    {
        await using var command = CreateCommand(connection, transaction, call);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        DbRoutineCall call,
        Func<DbDataReader, T> map,
        CancellationToken ct)
    {
        await using var connection = (DbConnection)factory.Create();
        await connection.OpenAsync(ct);
        return await QueryAsync(connection, transaction: null, call, map, ct);
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        DbConnection connection,
        DbTransaction? transaction,
        DbRoutineCall call,
        Func<DbDataReader, T> map,
        CancellationToken ct)
    {
        var rows = new List<T>();
        await using var command = CreateCommand(connection, transaction, call);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        DbRoutineCall call,
        Func<DbDataReader, T> map,
        CancellationToken ct)
    {
        await using var connection = (DbConnection)factory.Create();
        await connection.OpenAsync(ct);
        return await QuerySingleOrDefaultAsync(connection, transaction: null, call, map, ct);
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        DbConnection connection,
        DbTransaction? transaction,
        DbRoutineCall call,
        Func<DbDataReader, T> map,
        CancellationToken ct)
    {
        await using var command = CreateCommand(connection, transaction, call);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return default;
        }

        return map(reader);
    }

    private static DbCommand CreateCommand(
        DbConnection connection,
        DbTransaction? transaction,
        DbRoutineCall call)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = call.ToCommandText();

        foreach (var parameter in call.Parameters)
        {
            var dbParameter = command.CreateParameter();
            dbParameter.ParameterName = $"@{parameter.Name}";
            dbParameter.Value = parameter.Value ?? DBNull.Value;
            command.Parameters.Add(dbParameter);
        }

        return command;
    }

    private static T? ConvertScalar<T>(object? scalar)
    {
        if (scalar is null or DBNull)
        {
            return default;
        }

        if (scalar is T typed)
        {
            return typed;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T?)Convert.ChangeType(scalar, targetType, CultureInfo.InvariantCulture);
    }
}
