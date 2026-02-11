using Npgsql;
using System.Data;

namespace VarPrice.Web.Storage;

public interface IPgConnectionFactory
{
    IDbConnection Create();
}

public sealed class PgConnectionFactory(IConfiguration cfg) : IPgConnectionFactory
{
    public IDbConnection Create()
    {
        var cs = cfg.GetConnectionString("Postgres")!;
        return new NpgsqlConnection(cs);
    }
}
