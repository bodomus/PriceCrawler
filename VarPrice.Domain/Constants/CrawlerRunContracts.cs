namespace VarPrice.Domain.Constants;

public static class CrawlerRunTypes
{
    public const string CatalogRefresh = "catalog-refresh";
    public const string PriceCollection = "price-collection";
    public const string Legacy = "legacy";
}

public static class CrawlerRunStages
{
    public const string Discovery = "discovery";
    public const string CatalogUpsert = "catalog-upsert";
    public const string CatalogDeactivation = "catalog-deactivation";
    public const string CatalogSelection = "catalog-selection";
    public const string QueueEnqueue = "queue-enqueue";
    public const string QueueProcessing = "queue-processing";
    public const string RunFinalization = "run-finalization";
}
