using System.Data;

namespace PriceCrawler.Infrastructure.Persistence;

public interface IPgConnectionFactory
{
    IDbConnection Create();
}
