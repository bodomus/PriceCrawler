namespace VarPrice.Infrastructure.Crawler;

public interface ICategorySeedProvider
{
    Task<IReadOnlyList<CategorySeedUrl>> GetSeedsAsync(CancellationToken ct);
}
