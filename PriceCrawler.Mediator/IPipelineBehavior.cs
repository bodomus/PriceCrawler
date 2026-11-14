namespace PriceCrawler.Mediator;

/// <summary>Runs the next step of the pipeline: the next behavior or, at the end, the handler.</summary>
/// <typeparam name="TResponse">Type of the request result.</typeparam>
/// <param name="cancellationToken">Cancels the rest of the pipeline.</param>
/// <returns>The result of the request.</returns>
public delegate Task<TResponse> PipelineStep<TResponse>(CancellationToken cancellationToken);

/// <summary>
/// Wraps the handling of requests with cross-cutting logic such as logging, validation or
/// transactions. Behaviors run in registration order: the first registered one is the outermost.
/// </summary>
/// <typeparam name="TRequest">Type of the request.</typeparam>
/// <typeparam name="TResponse">Type of the request result.</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Runs the behavior around the rest of the pipeline.</summary>
    /// <param name="request">The request being handled.</param>
    /// <param name="nextStep">The rest of the pipeline; a behavior may skip it to short-circuit.</param>
    /// <param name="cancellationToken">Cancels the handling.</param>
    /// <returns>The result of the request.</returns>
    Task<TResponse> HandleAsync(
        TRequest request,
        PipelineStep<TResponse> nextStep,
        CancellationToken cancellationToken);
}
