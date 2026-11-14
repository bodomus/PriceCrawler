using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Npgsql;

namespace PriceCrawler.Api.Tests;

/// <summary>
/// Hosts the API against a real PostgreSQL: a fresh database is created on the test server (the one
/// that holds <c>varprice_test</c>), the API's own startup creates the schema (<c>Ensure</c> in the
/// <c>Test</c> environment), and the database is dropped afterwards. A separate database keeps these
/// tests away from the Web.Tests suite, which truncates <c>varprice_test</c> while it runs.
/// </summary>
public sealed class PostgresApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Server of the integration tests; override with PRICECRAWLER_TEST_POSTGRES.</summary>
    private const string DefaultServer = "Host=localhost;Port=55432;Database=varprice_test;Username=var;Password=myPassword";

    private readonly string _databaseName = $"pricecrawler_api_test_{Guid.NewGuid():N}";

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var server = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("PRICECRAWLER_TEST_POSTGRES") ?? DefaultServer);
        await ExecuteOnServerAsync(server, $"create database \"{_databaseName}\";");
        server.Database = _databaseName;
        server.Pooling = false;
        ConnectionString = server.ConnectionString;

        // Starting the host runs the schema startup service, which creates schema version 2.
        _ = Server;
        await TestData.SeedAsync(ConnectionString);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        var server = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres" };
        await ExecuteOnServerAsync(server, $"drop database if exists \"{_databaseName}\" with (force);");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        // UseSetting wins over environment variables such as CI's ConnectionStrings__Postgres.
        builder.UseSetting("ConnectionStrings:Postgres", ConnectionString);
        builder.UseSetting("DatabaseSchema:StartupMode", "Ensure");
    }

    private static async Task ExecuteOnServerAsync(NpgsqlConnectionStringBuilder server, string sql)
    {
        var admin = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres", Pooling = false };
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
