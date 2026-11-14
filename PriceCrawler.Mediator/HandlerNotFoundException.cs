namespace PriceCrawler.Mediator;

/// <summary>Thrown when a request is sent but no handler is registered for its type.</summary>
public sealed class HandlerNotFoundException : InvalidOperationException
{
    /// <summary>Creates the exception with a default message.</summary>
    public HandlerNotFoundException()
        : base("No handler is registered for the request.")
    {
    }

    /// <summary>Creates the exception with a custom message.</summary>
    /// <param name="message">Error message.</param>
    public HandlerNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a custom message and the error that caused it.</summary>
    /// <param name="message">Error message.</param>
    /// <param name="innerException">The error that caused this one.</param>
    public HandlerNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception for a request type without a handler.</summary>
    /// <param name="requestType">Type of the request that has no handler.</param>
    /// <returns>The exception.</returns>
    public static HandlerNotFoundException For(Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        return new HandlerNotFoundException(
            $"No handler is registered for request '{requestType.FullName}'. " +
            "Register its assembly with AddMediator(...).");
    }
}
