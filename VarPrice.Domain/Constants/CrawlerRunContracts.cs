namespace VarPrice.Domain.Constants;

public static class CrawlerRunTypes
{
    public const string CatalogRefresh = "catalog-refresh";
    public const string PriceCollection = "price-collection";
    public const string Legacy = "legacy";

    public static bool IsSupported(string value) => value is CatalogRefresh or PriceCollection or Legacy;
}

public static class CrawlerRunStatuses
{
    public const string Running = "running";
    public const string Ok = "ok";
    public const string Error = "error";

    public static bool IsSupported(string value) => value is Running or Ok or Error;
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
