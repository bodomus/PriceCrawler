# MPC-60 Review

## Summary

Refactored product URL discovery so Varus category seeds are the default phase-1 discovery strategy instead of a sitemap fallback. The crawler now selects discovery through `Crawler:DiscoveryMode`, with `CategorySeeds` as the default and explicit extension points for `Api` and `Sitemap`.

## Files changed

- `PriceCrawler.Application/Abstractions/IProductUrlDiscoveryStrategy.cs`
- `PriceCrawler.Application/Abstractions/IProductUrlDiscoveryStrategyFactory.cs`
- `PriceCrawler.Application/Models/ProductDiscoveryItem.cs`
- `PriceCrawler.Application/Models/ProductUrlDiscoveryModes.cs`
- `PriceCrawler.Application/Models/CrawlerOptions.cs`
- `PriceCrawler.Application/Models/ProductUrlDiscoveryResult.cs`
- `PriceCrawler.Application/UseCases/ProductUrlDiscoveryService.cs`
- `PriceCrawler.Application/UseCases/SitemapProductUrlDiscoveryStrategy.cs`
- `PriceCrawler.Application/UseCases/ApiProductUrlDiscoveryStrategy.cs`
- `PriceCrawler.Application/UseCases/SitemapProductUrlDiscoverySource.cs`
- `PriceCrawler.Application/UseCases/RunCrawlerUseCase.cs`
- `PriceCrawler.Infrastructure/Crawler/ProductUrlDiscoveryStrategyFactory.cs`
- `PriceCrawler.Infrastructure/Crawler/CategoryProductUrlDiscoverySource.cs`
- `PriceCrawler.Infrastructure/Crawler/CategorySeedProvider.cs`
- `PriceCrawler.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- `PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs`
- `PriceCrawler.Worker/appsettings.json`
- `PriceCrawler.Web/appsettings.json`
- `README.md`

## New interfaces/classes added

- `IProductUrlDiscoveryStrategy`
- `IProductUrlDiscoveryStrategyFactory`
- `ProductDiscoveryItem`
- `ProductUrlDiscoveryModes`
- `ProductUrlDiscoveryStrategyFactory`
- `CategorySeedProductUrlDiscoveryStrategy`
- `SitemapProductUrlDiscoveryStrategy`
- `ApiProductUrlDiscoveryStrategy`

## Existing behavior refactored

- `ProductUrlDiscoveryService` no longer tries sitemap first and then category fallback.
- Strategy selection now happens through `ProductUrlDiscoveryStrategyFactory`.
- `CategorySeeds` is selected when `Crawler:DiscoveryMode` is missing or empty.
- `Sitemap` remains available as an explicit mode.
- `Api` is registered as a future extension point and throws a clear not-implemented error if selected.
- Category discovery keeps seed loading, page loading, link extraction, pagination, normalization, and orchestration split across existing components.
- Category page logs now include `SeedName`, `SeedUrl`, `PageUrl`, `PageNumber`, `ProductUrlsFound`, `NewProductUrlsFound`, `MaxCategoryPagesPerSeed`, and `StopReason`.

## Configuration changes

- Added `Crawler:DiscoveryMode`.
- Default discovery mode is `CategorySeeds`.
- Updated `Crawler:MaxCategoryPagesPerSeed` default from `3` to `10`.
- Updated configured seed file path to `PriceCrawler.Worker/config/category-seed-urls.varus.json`.
- Root `config/category-seed-urls.varus.json` is no longer used.

## Tests added/updated

- Added strategy factory coverage for missing mode, `CategorySeeds`, and unsupported mode.
- Updated service tests to verify selected category strategy is used directly instead of sitemap fallback behavior.
- Updated category seed provider tests so missing or malformed seed files throw clear errors.
- Existing category discovery tests continue to cover validation, deduplication, pagination, and stop conditions.

## Validation

- `dotnet build` passed.
- `dotnet test` passed: 94 tests.

Both commands emitted `NU1900` warnings because NuGet vulnerability metadata could not be loaded from `https://api.nuget.org/v3/index.json`; compilation and tests still completed successfully.

## Risks and notes

- `Crawler:DiscoveryMode=Api` is intentionally not implemented yet.
- The Worker and Web configs both point to the Worker seed file location so `PriceCrawler.Worker/config/category-seed-urls.varus.json` is the single active category seed file.
- Existing compatibility interfaces for sitemap/category source discovery remain so older tests or callers can still resolve them.
