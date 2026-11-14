namespace PriceCrawler.Mediator.Validation;

/// <summary>
/// Checks a request before its handler runs. A request may have several validators; all of them
/// run and their failures are reported together by <see cref="ValidationBehavior{TRequest, TResponse}"/>.
/// </summary>
/// <typeparam name="TRequest">Type of the checked request.</typeparam>
public interface IRequestValidator<in TRequest>
{
    /// <summary>Checks the request.</summary>
    /// <param name="request">The request to check.</param>
    /// <returns>The broken rules; empty when the request is valid.</returns>
    IEnumerable<ValidationFailure> Validate(TRequest request);
}
