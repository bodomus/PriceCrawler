namespace PriceCrawler.Infrastructure.Persistence;

/// <summary>Defines whether application startup may initialize or only validate the database schema.</summary>
public enum DatabaseSchemaStartupMode
{
    ValidateOnly,
    Ensure
}
