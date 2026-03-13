namespace VarPrice.Application.Models;

public sealed record ProductExtractIssue(
    string Stage,
    string ErrorCode,
    int? HttpStatus,
    string? Message,
    string? DetailsJson,
    bool IsTransient,
    bool IsCritical);
