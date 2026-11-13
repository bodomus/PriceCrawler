using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

using PriceCrawler.Mediator.Validation;

namespace PriceCrawler.Api.ErrorHandling;

/// <summary>
/// Turns exceptions into RFC 9457 problem responses: invalid requests become 400 with the broken
/// rules per field; anything else is left to the default handler (500 without details).
/// </summary>
/// <param name="problemDetails">Writes problem responses.</param>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    /// <inheritdoc/>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not RequestValidationException validation)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ValidationProblemDetails(validation.ToDictionary())
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "The request is invalid.",
            },
        }).ConfigureAwait(false);
    }
}
