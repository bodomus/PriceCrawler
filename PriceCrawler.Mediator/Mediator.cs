using System.Collections.Concurrent;

using PriceCrawler.Mediator.Internal;

namespace PriceCrawler.Mediator;

/// <summary>
/// Default <see cref="ISender"/>: finds the handler and the pipeline behaviors of a request in the
/// DI container and runs them. Resolve it from a request scope so scoped handlers work.
/// </summary>
/// <param name="serviceProvider">Scope the handler and behaviors are resolved from.</param>
public sealed class Mediator(IServiceProvider serviceProvider) : ISender
{
    // Reflection runs once per (request, response) pair; later sends reuse the typed dispatcher.
    private static readonly ConcurrentDictionary<(Type Request, Type Response), RequestDispatcher> Dispatchers = new();

    /// <inheritdoc/>
    public Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RequestDispatcher dispatcher = Dispatchers.GetOrAdd(
            (request.GetType(), typeof(TResponse)),
            static key => RequestDispatcher.Create(key.Request, key.Response));
        return ((RequestDispatcher<TResponse>)dispatcher).DispatchAsync(request, serviceProvider, cancellationToken);
    }
}
