namespace PriceCrawler.Infrastructure.Persistence;

/// <summary>Describes database schema metadata and compatibility.</summary>
public sealed record DatabaseSchemaValidationResult(
    bool MetadataTableExists,
    int? ActualVersion,
    int ExpectedVersion)
{
    public bool IsCompatible => MetadataTableExists && ActualVersion == ExpectedVersion;
}

