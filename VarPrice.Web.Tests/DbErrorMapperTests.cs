using VarPrice.Web.Storage.Db;

namespace VarPrice.Web.Tests;

public sealed class DbErrorMapperTests
{
    [Fact]
    public void Map_ConstraintSqlState_Returns_DbConstraint_Code()
    {
        var mapper = new DbErrorMapper();

        var error = mapper.Map(
            new FakeSqlStateException("23505", "duplicate key value violates unique constraint"),
            "PgCrawlerRepository.UpsertProductAsync",
            "corr-1");

        Assert.Equal(DbErrorCodes.Constraint, error.Code);
        Assert.Equal("corr-1", error.CorrelationId);
    }

    [Fact]
    public void Map_TimeoutException_Returns_DbTimeout_Code()
    {
        var mapper = new DbErrorMapper();

        var error = mapper.Map(
            new TimeoutException("db timeout"),
            "PgCrawlerRepository.StartRunAsync",
            "corr-2");

        Assert.Equal(DbErrorCodes.Timeout, error.Code);
        Assert.Equal("corr-2", error.CorrelationId);
    }

    private sealed class FakeSqlStateException(string sqlState, string message) : Exception(message)
    {
        public string SqlState { get; } = sqlState;
    }
}
