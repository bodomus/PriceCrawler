namespace VarPrice.Infrastructure.Crawler;

public interface ICategoryPaginationStrategy
{
    Uri? GetNextPageUrl(string html, Uri currentPageUrl, ISet<string> visitedPageUrls);

    string? ResolveStopReason(int newProductUrls, Uri? nextPageUrl, int pageNumber, int maxPagesPerSeed);
}
