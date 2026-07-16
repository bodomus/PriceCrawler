namespace PriceCrawler.Infrastructure.Persistence;

/// <summary>Controls database initialization and startup validation.</summary>
public sealed class DatabaseSchemaOptions
{
    public const string SectionName = "DatabaseSchema";

    public bool AllowAutomaticInitialization { get; set; }

    public bool ValidateOnStartup { get; set; } = true;
}

