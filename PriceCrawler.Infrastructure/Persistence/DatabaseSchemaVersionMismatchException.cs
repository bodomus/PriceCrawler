namespace PriceCrawler.Infrastructure.Persistence;

/// <summary>Thrown when the connected database is not compatible with this application release.</summary>
public sealed class DatabaseSchemaVersionMismatchException : InvalidOperationException
{
    public DatabaseSchemaVersionMismatchException(string message, DatabaseSchemaValidationResult validationResult)
        : base(message)
    {
        ValidationResult = validationResult;
    }

    public DatabaseSchemaValidationResult ValidationResult { get; }
}

