using System.Diagnostics;

using Microsoft.Extensions.Logging;

using PriceCrawler.Mediator.Validation;

namespace PriceCrawler.Mediator.Behaviors;

/// <summary>Logs the start, duration and failure of every request; invalid requests are logged without a stack trace.</summary>
/// <typeparam name="TRequest">Type of the request.</typeparam>
/// <typeparam name="TResponse">Type of the request result.</typeparam>
/// <param name="logger">Logger named after this behavior.</param>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> HandleAsync(
        TRequest request,
        PipelineStep<TResponse> nextStep,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nextStep);

        string requestName = typeof(TRequest).Name;
        MediatorLog.Handling(logger, requestName);
        long started = Stopwatch.GetTimestamp();
        try
        {
            TResponse response = await nextStep(cancellationToken).ConfigureAwait(false);
            double elapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            MediatorLog.Handled(logger, requestName, elapsedMilliseconds);
            return response;
        }
        catch (RequestValidationException exception)
        {
            // An invalid request is the caller's mistake, not a failure: no stack trace in the log.
            int failureCount = exception.Failures.Count;
            MediatorLog.Rejected(logger, requestName, failureCount);
            throw;
        }
        catch (Exception exception)
        {
            double elapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            MediatorLog.Failed(logger, requestName, elapsedMilliseconds, exception);
            throw;
        }
    }
}
