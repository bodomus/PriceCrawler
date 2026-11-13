namespace PriceCrawler.Mediator.Validation;

/// <summary>Thrown by <see cref="ValidationBehavior{TRequest, TResponse}"/> when a request breaks rules.</summary>
public sealed class RequestValidationException : Exception
{
    /// <summary>Creates the exception without failures.</summary>
    public RequestValidationException()
        : this("The request is invalid.", [])
    {
    }

    /// <summary>Creates the exception with a custom message and no failures.</summary>
    /// <param name="message">Error message.</param>
    public RequestValidationException(string message)
        : this(message, [])
    {
    }

    /// <summary>Creates the exception with a custom message and the error that caused it.</summary>
    /// <param name="message">Error message.</param>
    /// <param name="innerException">The error that caused this one.</param>
    public RequestValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Failures = [];
    }

    /// <summary>Creates the exception for the broken rules of a request.</summary>
    /// <param name="message">Error message.</param>
    /// <param name="failures">The broken rules.</param>
    public RequestValidationException(string message, IReadOnlyList<ValidationFailure> failures)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(failures);
        Failures = failures;
    }

    /// <summary>The broken rules.</summary>
    public IReadOnlyList<ValidationFailure> Failures { get; }

    /// <summary>Groups the failures by property, the shape RFC 9457 validation problems use.</summary>
    /// <returns>Messages per property name.</returns>
    public IDictionary<string, string[]> ToDictionary() =>
        Failures
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.Message).ToArray(),
                StringComparer.Ordinal);
}
