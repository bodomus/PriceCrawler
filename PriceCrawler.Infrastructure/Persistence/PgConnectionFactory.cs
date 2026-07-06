using Npgsql;
using System.Data;
using Microsoft.Extensions.Configuration;

namespace PriceCrawler.Infrastructure.Persistence;

public sealed class PgConnectionFactory(IConfiguration cfg) : IPgConnectionFactory
{
    public IDbConnection Create()
    {
        var cs = cfg.GetConnectionString("Postgres")!;
        return new NpgsqlConnection(cs);
    }
}
