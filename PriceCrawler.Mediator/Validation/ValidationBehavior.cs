namespace PriceCrawler.Mediator.Validation;

/// <summary>
/// Runs every <see cref="IRequestValidator{TRequest}"/> of a request before its handler and stops
/// the pipeline with <see cref="RequestValidationException"/> when any rule is broken.
/// </summary>
/// <typeparam name="TRequest">Type of the request.</typeparam>
/// <typeparam name="TResponse">Type of the request result.</typeparam>
/// <param name="validators">Validators registered for the request type.</param>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IRequestValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc/>
    public Task<TResponse> HandleAsync(
        TRequest request,
        PipelineStep<TResponse> nextStep,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nextStep);

        List<ValidationFailure> failures = [.. validators.SelectMany(validator => validator.Validate(request))];
        if (failures.Count > 0)
        {
            throw new RequestValidationException($"Request '{typeof(TRequest).Name}' is invalid.", failures);
        }

        return nextStep(cancellationToken);
    }
}
