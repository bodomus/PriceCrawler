# MPC-60 Review

## Summary

Refactored product URL discovery so Varus category seeds are the default phase-1 discovery strategy instead of a sitemap fallback. The crawler now selects discovery through `Crawler:DiscoveryMode`, with `CategorySeeds` as the default and explicit extension points for `Api` and `Sitemap`.

## Files changed

- `VarPrice.Application/Abstractions/IProductUrlDiscoveryStrategy.cs`
- `VarPrice.Application/Abstractions/IProductUrlDiscoveryStrategyFactory.cs`
- `VarPrice.Application/Models/ProductDiscoveryItem.cs`
- `VarPrice.Application/Models/ProductUrlDiscoveryModes.cs`
- `VarPrice.Application/Models/CrawlerOptions.cs`
- `VarPrice.Application/Models/ProductUrlDiscoveryResult.cs`
- `VarPrice.Application/UseCases/ProductUrlDiscoveryService.cs`
- `VarPrice.Application/UseCases/SitemapProductUrlDiscoveryStrategy.cs`
- `VarPrice.Application/UseCases/ApiProductUrlDiscoveryStrategy.cs`
- `VarPrice.Application/UseCases/SitemapProductUrlDiscoverySource.cs`
- `VarPrice.Application/UseCases/RunCrawlerUseCase.cs`
- `VarPrice.Infrastructure/Crawler/ProductUrlDiscoveryStrategyFactory.cs`
- `VarPrice.Infrastructure/Crawler/CategoryProductUrlDiscoverySource.cs`
- `VarPrice.Infrastructure/Crawler/CategorySeedProvider.cs`
- `VarPrice.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- `VarPrice.Web.Tests/ProductUrlDiscoveryTests.cs`
- `VarPrice.Worker/appsettings.json`
- `VarPrice.Web/appsettings.json`
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
- Updated configured seed file path to `VarPrice.Worker/config/category-seed-urls.varus.json`.
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
- The Worker and Web configs both point to the Worker seed file location so `VarPrice.Worker/config/category-seed-urls.varus.json` is the single active category seed file.
- Existing compatibility interfaces for sitemap/category source discovery remain so older tests or callers can still resolve them.
