namespace PriceCrawler.Mediator;

/// <summary>Handles one request type. Every request type has exactly one handler.</summary>
/// <typeparam name="TRequest">Type of the handled request.</typeparam>
/// <typeparam name="TResponse">Type of the result.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Handles the request.</summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="cancellationToken">Cancels the handling.</param>
    /// <returns>The result of the request.</returns>
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>Handles a query: reads data and changes nothing.</summary>
/// <typeparam name="TQuery">Type of the handled query.</typeparam>
/// <typeparam name="TResponse">Type of the data that is read.</typeparam>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>;

/// <summary>Handles a command that returns a result.</summary>
/// <typeparam name="TCommand">Type of the handled command.</typeparam>
/// <typeparam name="TResponse">Type of the result.</typeparam>
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>;

/// <summary>Handles a command that returns nothing.</summary>
/// <typeparam name="TCommand">Type of the handled command.</typeparam>
public interface ICommandHandler<in TCommand> : ICommandHandler<TCommand, Unit>
    where TCommand : ICommand;
