using Npgsql;

namespace PriceCrawler.Web.Tests;

internal sealed class TemporaryPostgresDatabase : IAsyncDisposable
{
    private readonly string _databaseName;
    private readonly List<string> _roles = [];
    private bool _disposed;

    private TemporaryPostgresDatabase(string databaseName, string connectionString)
    {
        _databaseName = databaseName;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static async Task<TemporaryPostgresDatabase> CreateAsync(string prefix = "mpc80")
    {
        var template = new NpgsqlConnectionStringBuilder(PostgresIntegrationFixture.ConnectionString);
        var databaseName = $"pricecrawler_{prefix}_test_{Guid.NewGuid():N}";
        var admin = new NpgsqlConnectionStringBuilder(template.ConnectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"create database {QuoteIdentifier(databaseName)};", connection);
        await command.ExecuteNonQueryAsync();

        template.Database = databaseName;
        return new TemporaryPostgresDatabase(databaseName, template.ConnectionString);
    }

    public async Task ExecuteFileAsync(string path, CancellationToken ct = default)
        => await ExecuteAsync(await File.ReadAllTextAsync(path, ct), ct);

    public async Task ExecuteAsync(string sql, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<T> ScalarAsync<T>(string sql, CancellationToken ct = default)
        => await ScalarAsync<T>(ConnectionString, sql, ct);

    public static async Task<T> ScalarAsync<T>(
        string connectionString,
        string sql,
        CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        var value = await command.ExecuteScalarAsync(ct);
        return (T)Convert.ChangeType(value!, typeof(T));
    }

    public async Task<string> CreateReadOnlyRuntimeRoleAsync()
    {
        var roleName = $"pricecrawler_mpc80_ro_{Guid.NewGuid():N}";
        var password = $"Mpc80_{Guid.NewGuid():N}";
        _roles.Add(roleName);
        await ExecuteAsync($"""
                           create role {QuoteIdentifier(roleName)} login password '{password}';
                           grant connect on database {QuoteIdentifier(_databaseName)} to {QuoteIdentifier(roleName)};
                           grant usage on schema public to {QuoteIdentifier(roleName)};
                           grant select on public.schema_version to {QuoteIdentifier(roleName)};
                           """);
        var runtime = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Username = roleName,
            Password = password,
            Pooling = false
        };
        return runtime.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        var template = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(template.ConnectionString);
        await connection.OpenAsync();
        await using (var terminateCommand = new NpgsqlCommand("""
                                                              select pg_terminate_backend(pid)
                                                              from pg_stat_activity
                                                              where datname = @database_name
                                                                and pid <> pg_backend_pid();
                                                              """, connection))
        {
            terminateCommand.Parameters.AddWithValue("database_name", _databaseName);
            await terminateCommand.ExecuteNonQueryAsync();
        }

        await using (var dropCommand = new NpgsqlCommand(
                         $"drop database if exists {QuoteIdentifier(_databaseName)};",
                         connection))
        {
            await dropCommand.ExecuteNonQueryAsync();
        }

        foreach (var role in _roles)
        {
            await using var dropRoleCommand = new NpgsqlCommand(
                $"drop role if exists {QuoteIdentifier(role)};",
                connection);
            await dropRoleCommand.ExecuteNonQueryAsync();
        }
    }

    private static string QuoteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
