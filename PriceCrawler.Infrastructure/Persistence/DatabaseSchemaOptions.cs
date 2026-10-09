namespace PriceCrawler.Infrastructure.Persistence;

/// <summary>Controls the single database schema operation performed during application startup.</summary>
public sealed class DatabaseSchemaOptions
{
    public const string SectionName = "DatabaseSchema";

    public DatabaseSchemaStartupMode StartupMode { get; set; } = DatabaseSchemaStartupMode.ValidateOnly;
}
