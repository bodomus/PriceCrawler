namespace PriceCrawler.Web.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresIntegrationCollection
{
    public const string Name = "Postgres integration";
}
