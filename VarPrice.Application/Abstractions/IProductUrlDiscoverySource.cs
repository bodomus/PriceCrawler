namespace VarPrice.Application.Abstractions;

public interface IProductUrlDiscoverySource
{
    Task<IReadOnlyCollection<Uri>> DiscoverProductUrlsAsync(CancellationToken ct);
}
