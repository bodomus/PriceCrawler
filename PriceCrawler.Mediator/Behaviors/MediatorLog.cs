using Microsoft.Extensions.Logging;

namespace PriceCrawler.Mediator.Behaviors;

/// <summary>Source-generated log messages of the mediator pipeline.</summary>
internal static partial class MediatorLog
{
    /// <summary>Logs that a request entered the pipeline.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="requestName">Short name of the request type.</param>
    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug, Message = "Handling {RequestName}")]
    public static partial void Handling(ILogger logger, string requestName);

    /// <summary>Logs that a request was handled.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="requestName">Short name of the request type.</param>
    /// <param name="elapsedMilliseconds">Time the pipeline took.</param>
    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Handled {RequestName} in {ElapsedMilliseconds:0.0} ms")]
    public static partial void Handled(ILogger logger, string requestName, double elapsedMilliseconds);

    /// <summary>Logs that a request failed.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="requestName">Short name of the request type.</param>
    /// <param name="elapsedMilliseconds">Time until the failure.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "{RequestName} failed after {ElapsedMilliseconds:0.0} ms")]
    public static partial void Failed(ILogger logger, string requestName, double elapsedMilliseconds, Exception exception);

    /// <summary>Logs that a request was rejected by validation.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="requestName">Short name of the request type.</param>
    /// <param name="failureCount">Number of broken rules.</param>
    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "{RequestName} rejected: {FailureCount} validation failure(s)")]
    public static partial void Rejected(ILogger logger, string requestName, int failureCount);
}
