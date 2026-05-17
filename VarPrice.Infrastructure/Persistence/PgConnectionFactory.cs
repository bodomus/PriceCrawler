using System.Data;

using Npgsql;

namespace VarPrice.Infrastructure.Persistence;

public sealed class PgConnectionFactory(SelectedDatabase database) : IPgConnectionFactory
{
    public IDbConnection Create()
    {
        return new NpgsqlConnection(database.ConnectionString);
    }
}
