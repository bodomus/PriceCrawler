namespace PriceCrawler.Mediator.Validation;

/// <summary>One rule a request broke.</summary>
/// <param name="PropertyName">Name of the request property that is invalid.</param>
/// <param name="Message">What is wrong, in a form that can be shown to a user.</param>
public sealed record ValidationFailure(string PropertyName, string Message);
