using Microsoft.Extensions.DependencyInjection;

namespace PriceCrawler.Mediator.Internal;

/// <summary>Untyped base of the cached per-request-type dispatchers.</summary>
internal abstract class RequestDispatcher
{
    /// <summary>Creates the typed dispatcher for a request type.</summary>
    /// <param name="requestType">Concrete request type.</param>
    /// <param name="responseType">Result type the request is sent with.</param>
    /// <returns>A <see cref="RequestDispatcher{TRequest, TResponse}"/> for the pair.</returns>
    public static RequestDispatcher Create(Type requestType, Type responseType)
    {
        Type dispatcherType = typeof(RequestDispatcher<,>).MakeGenericType(requestType, responseType);
        return (RequestDispatcher)Activator.CreateInstance(dispatcherType)!;
    }
}

/// <summary>Dispatcher that knows only the result type, so the mediator can call it without reflection.</summary>
/// <typeparam name="TResponse">Type of the request result.</typeparam>
internal abstract class RequestDispatcher<TResponse> : RequestDispatcher
{
    /// <summary>Resolves the handler and behaviors and runs the pipeline.</summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="serviceProvider">Scope to resolve services from.</param>
    /// <param name="cancellationToken">Cancels the handling.</param>
    /// <returns>The result of the request.</returns>
    public abstract Task<TResponse> DispatchAsync(
        IRequest<TResponse> request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken);
}

/// <summary>Fully typed dispatcher for one request type.</summary>
/// <typeparam name="TRequest">Type of the request.</typeparam>
/// <typeparam name="TResponse">Type of the request result.</typeparam>
internal sealed class RequestDispatcher<TRequest, TResponse> : RequestDispatcher<TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc/>
    public override Task<TResponse> DispatchAsync(
        IRequest<TResponse> request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var typedRequest = (TRequest)request;
        IRequestHandler<TRequest, TResponse> handler =
            serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>()
            ?? throw HandlerNotFoundException.For(typeof(TRequest));

        PipelineStep<TResponse> pipeline = token => handler.HandleAsync(typedRequest, token);

        // Wrap from the last registered behavior outwards, so the first registered one runs first.
        IPipelineBehavior<TRequest, TResponse>[] behaviors =
            [.. serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>()];
        for (int index = behaviors.Length - 1; index >= 0; index--)
        {
            IPipelineBehavior<TRequest, TResponse> behavior = behaviors[index];
            PipelineStep<TResponse> next = pipeline;
            pipeline = token => behavior.HandleAsync(typedRequest, next, token);
        }

        return pipeline(cancellationToken);
    }
}
