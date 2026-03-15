namespace VarPrice.Web.Logging;

public interface ILoggingBootstrapper
{
    ILogger CreateLogger<T>();

    ILogger CreateLogger(string category);
}

public sealed class LoggingBootstrapper(ILoggerFactory loggerFactory) : ILoggingBootstrapper
{
    public ILogger CreateLogger<T>() => loggerFactory.CreateLogger<T>();

    public ILogger CreateLogger(string category) => loggerFactory.CreateLogger(category);
}
