namespace VarPrice.Application.Abstractions;

public interface IProductUrlDiscoveryService
{
    Task<IReadOnlyList<string>> DiscoverProductUrlsAsync(CancellationToken ct);
}
