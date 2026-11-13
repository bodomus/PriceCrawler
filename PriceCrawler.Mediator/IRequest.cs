namespace PriceCrawler.Mediator;

/// <summary>A message sent through <see cref="ISender"/> to exactly one handler.</summary>
/// <typeparam name="TResponse">Type of the handler's result.</typeparam>
public interface IRequest<out TResponse>;

/// <summary>A request that reads data and changes nothing (the Q in CQRS).</summary>
/// <typeparam name="TResponse">Type of the data that is read.</typeparam>
public interface IQuery<out TResponse> : IRequest<TResponse>;

/// <summary>A request that changes state and returns a result (the C in CQRS).</summary>
/// <typeparam name="TResponse">Type of the result, for example the id of a created entity.</typeparam>
public interface ICommand<out TResponse> : IRequest<TResponse>;

/// <summary>A request that changes state and returns nothing.</summary>
public interface ICommand : ICommand<Unit>;
