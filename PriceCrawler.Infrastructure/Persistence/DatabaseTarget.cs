using Microsoft.Extensions.Configuration;

using Npgsql;

namespace PriceCrawler.Infrastructure.Persistence;

/// <summary>
/// Describes which PostgreSQL database the configured <c>ConnectionStrings:Postgres</c> points to,
/// without the password, so every Web/Worker start logs its target database (MPC-90).
/// </summary>
public static class DatabaseTarget
{
    public static string Describe(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "not configured";
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return $"Host={builder.Host}; Port={builder.Port}; Database={builder.Database}; Username={builder.Username}";
    }
}
