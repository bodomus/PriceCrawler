namespace VarPrice.Web.Storage.Db;

public sealed record DbError(
    string Code,
    string UserMessage,
    string? TechnicalDetails = null,
    string? Operation = null,
    string? CorrelationId = null
);
