using Npgsql;
using System.Data;
using Microsoft.Extensions.Configuration;

namespace VarPrice.Infrastructure.Persistence;

public sealed class PgConnectionFactory(IConfiguration cfg) : IPgConnectionFactory
{
    public IDbConnection Create()
    {
        var cs = cfg.GetConnectionString("Postgres")!;
        return new NpgsqlConnection(cs);
    }
}
