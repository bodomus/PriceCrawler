namespace VarPrice.Infrastructure.Crawler;

public static class CategoryDiscoveryStopReasons
{
    public const string PageUnavailable = "PageUnavailable";
    public const string NoNewProductUrls = "NoNewProductUrls";
    public const string NoNextPage = "NoNextPage";
    public const string MaxCategoryPagesPerSeed = "MaxCategoryPagesPerSeed";
}
