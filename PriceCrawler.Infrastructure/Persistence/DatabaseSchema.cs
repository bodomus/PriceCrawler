namespace PriceCrawler.Infrastructure.Persistence;

/// <summary>Defines the database schema supported by this application release.</summary>
public static class DatabaseSchema
{
    /// <summary>Schema version this release reads and writes; startup fails on any other version.</summary>
    public const int ExpectedVersion = 2;

    /// <summary>Version created by <see cref="BaselineMigrationName"/> on an empty database.</summary>
    public const int BaselineVersion = 1;

    public const string BaselineMigrationName = "0001_baseline";

    public const string BaselineApplicationVersion = "v0.4.1-alpha";
}
