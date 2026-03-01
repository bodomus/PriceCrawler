namespace VarPrice.Application.Abstractions;

public interface IProductUrlSource
{
    Task<IReadOnlyList<string>> GetProductUrlsAsync(string sitemapIndexUrl, CancellationToken ct);
}
