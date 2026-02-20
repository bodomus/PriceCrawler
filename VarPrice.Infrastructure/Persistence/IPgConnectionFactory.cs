using System.Data;

namespace VarPrice.Infrastructure.Persistence;

public interface IPgConnectionFactory
{
    IDbConnection Create();
}
