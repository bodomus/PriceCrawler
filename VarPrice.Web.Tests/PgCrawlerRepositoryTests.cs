using System.Data;
using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using VarPrice.Web.Storage;
using VarPrice.Web.Storage.Db;

namespace VarPrice.Web.Tests;

public sealed class PgCrawlerRepositoryTests
{
    [Fact]
    public async Task StartRunAsync_WhenConnectionUnavailable_ReturnsFail_AndLogsError()
    {
        var logger = new ListLogger<DbExecutor>();
        var mapper = new DbErrorMapper();
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { TraceIdentifier = "corr-test-1" }
        };
        var executor = new DbExecutor(mapper, accessor, logger);

        var factory = new ThrowingConnectionFactory(new TimeoutException("Database is not reachable"));
        var repository = new PgCrawlerRepository(factory, executor);

        var result = await repository.StartRunAsync("sitemap", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.NotNull(result.Error);
        Assert.Equal(DbErrorCodes.Timeout, result.Error!.Code);
        Assert.Equal("corr-test-1", result.Error.CorrelationId);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Exception is TimeoutException);
    }

    private sealed class ThrowingConnectionFactory(Exception exceptionToThrow) : IPgConnectionFactory
    {
        public IDbConnection Create() => new ThrowingDbConnection(exceptionToThrow);
    }

    private sealed class ThrowingDbConnection(Exception exceptionToThrow) : DbConnection
    {
#pragma warning disable CS8764
        public override string? ConnectionString { get; set; } = string.Empty;
#pragma warning restore CS8764
        public override string Database => "varprice";
        public override string DataSource => "localhost";
        public override string ServerVersion => "1";
        public override ConnectionState State => ConnectionState.Closed;

        public override void Open() => throw exceptionToThrow;

        public override Task OpenAsync(CancellationToken cancellationToken) => Task.FromException(exceptionToThrow);

        public override void Close() { }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
