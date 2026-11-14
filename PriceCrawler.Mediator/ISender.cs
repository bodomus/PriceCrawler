namespace PriceCrawler.Mediator;

/// <summary>Sends a request through the pipeline to its single handler.</summary>
public interface ISender
{
    /// <summary>Sends the request to its handler through the registered pipeline behaviors.</summary>
    /// <typeparam name="TResponse">Type of the request result.</typeparam>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Cancels the handling.</param>
    /// <returns>The result of the handler.</returns>
    /// <exception cref="HandlerNotFoundException">No handler is registered for the request type.</exception>
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}
