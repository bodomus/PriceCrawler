# MPC-60: Refactor Varus Discovery to CategorySeed Strategy After MPC-59

Generated: 23.05.2026 07:46 Kyiv  
Target: Codex  
Project: PriceCrawler / Varus

---

## Context

Branch/task `MPC-59` has already been merged.

`MPC-59` introduced or changed category-based product URL discovery for Varus.  
The current implementation now needs architectural stabilization.

Important business decision:

We no longer treat XML sitemap as the required primary source for Varus product URL discovery.

The file:

```text
category-seed-urls.varus.json
```

must become the main discovery source for phase 1.

This file contains category seed URLs like:

```json
{
  "Crawler": {
    "CategorySeedUrls": [
      {
        "name": "Здоровое питание и Эко товары",
        "url": "https://varus.ua/healthy"
      },
      {
        "name": "Органические продукты",
        "url": "https://varus.ua/organic-food"
      }
    ]
  }
}
```

Each URL points to a Varus category page where products are already displayed.

Later, when the Varus API is discovered and stabilized, it must be possible to add a second discovery strategy without rewriting the crawler pipeline.

---

## Goal

Refactor the product URL discovery architecture so that:

1. `category-seed-urls.varus.json` is treated as the primary phase-1 discovery source.
2. Category discovery is implemented as a strategy, not as sitemap fallback.
3. The crawler can later support multiple discovery strategies:
   - Category seed URL discovery
   - API discovery
   - Optional sitemap discovery, if still present
4. Existing `MPC-59` functionality must not be broken.
5. The current “fat class” discovery implementation must be split into smaller components.

---

## Required Architecture

Introduce or align to this abstraction:

```csharp
public interface IProductUrlDiscoveryStrategy
{
    Task<IReadOnlyCollection<ProductDiscoveryItem>> DiscoverAsync(
        CancellationToken cancellationToken);
}
```

If `ProductDiscoveryItem` already exists, reuse it.

If not, introduce a minimal model containing at least:

```csharp
public sealed record ProductDiscoveryItem(
    string Url,
    string? SourceName = null,
    string? SourceUrl = null);
```

The exact shape may be adjusted to match the existing domain/application model.

---

## Required Strategies

Implement the first real strategy:

```csharp
public sealed class CategorySeedProductUrlDiscoveryStrategy
    : IProductUrlDiscoveryStrategy
{
}
```

This strategy must orchestrate the category discovery flow but must not directly contain all responsibilities.

Future placeholder or extension point:

```csharp
public sealed class ApiProductUrlDiscoveryStrategy
    : IProductUrlDiscoveryStrategy
{
}
```

Do not implement real API calls yet.  
Only keep the architecture ready for it if useful.

Optional existing sitemap strategy:

```csharp
public sealed class SitemapProductUrlDiscoveryStrategy
    : IProductUrlDiscoveryStrategy
{
}
```

Only keep/refactor this if sitemap discovery already exists and is still used somewhere.  
Do not make sitemap mandatory.

---

## Required Factory

Introduce a factory for selecting the discovery strategy:

```csharp
public interface IProductUrlDiscoveryStrategyFactory
{
    IProductUrlDiscoveryStrategy Create();
}
```

Factory should select the strategy from configuration.

Add config option:

```json
{
  "Crawler": {
    "DiscoveryMode": "CategorySeeds"
  }
}
```

Supported values:

```text
CategorySeeds
Api
Sitemap
```

For now, the default should be:

```text
CategorySeeds
```

If `DiscoveryMode` is missing, use `CategorySeeds`.

If an unsupported mode is configured, throw a clear configuration error.

---

## Required Component Split

Do not keep all logic inside one discovery class.

Split category discovery into components similar to:

```csharp
public interface ICategorySeedProvider
{
    Task<IReadOnlyCollection<CategorySeedUrl>> GetSeedsAsync(
        CancellationToken cancellationToken);
}
```

```csharp
public sealed record CategorySeedUrl(
    string Name,
    string Url);
```

```csharp
public interface ICategoryPageLoader
{
    Task<string> LoadAsync(
        string pageUrl,
        CancellationToken cancellationToken);
}
```

```csharp
public interface ICategoryProductLinkExtractor
{
    IReadOnlyCollection<string> ExtractProductUrls(
        string html,
        string pageUrl);
}
```

```csharp
public interface ICategoryPaginationStrategy
{
    string? GetNextPageUrl(
        string html,
        string currentPageUrl,
        int currentPageNumber);
}
```

The final naming can be adjusted to existing project conventions, but responsibilities must remain separated.

---

## Required Category Discovery Flow

The phase-1 discovery flow should be:

```text
category-seed-urls.varus.json
    -> ICategorySeedProvider
    -> CategorySeedProductUrlDiscoveryStrategy
    -> ICategoryPageLoader
    -> ICategoryProductLinkExtractor
    -> ICategoryPaginationStrategy
    -> URL filtering / normalization
    -> product URLs for crawler pipeline
```

---

## Pagination Requirements

Category discovery must support bounded pagination.

Add configuration:

```json
{
  "Crawler": {
    "MaxCategoryPagesPerSeed": 10
  }
}
```

Default value should be safe and bounded, for example:

```text
10
```

Stop pagination when any of these happens:

1. No next page URL is found.
2. No new product URLs are found on the current page.
3. `MaxCategoryPagesPerSeed` is reached.
4. Page load fails in a non-recoverable way.

---

## Logging Requirements

Add structured logs for category discovery.

Logs must include:

- `SeedName`
- `SeedUrl`
- `PageUrl`
- `PageNumber`
- `ProductUrlsFound`
- `NewProductUrlsFound`
- `MaxCategoryPagesPerSeed`
- Stop reason

Example stop reasons:

```text
NoNextPage
NoNewProductUrls
MaxPagesReached
PageLoadFailed
```

---

## Naming Requirement

Do not name the category discovery implementation as fallback.

Avoid names like:

```text
CategoryFallbackDiscoverySource
SitemapFallbackCategoryDiscovery
FallbackProductUrlDiscovery
```

Use names like:

```text
CategorySeedProductUrlDiscoveryStrategy
CategorySeedDiscoveryStrategy
CategorySeedProvider
```

Reason:

Category seed discovery is now the primary phase-1 Varus discovery mode, not a temporary fallback.

---

## Backward Compatibility

Because `MPC-59` is already merged:

1. Do not revert `MPC-59`.
2. Refactor on top of the merged code.
3. Preserve existing behavior where possible.
4. If existing settings are already used by `MPC-59`, migrate them carefully.
5. Existing tests must continue to pass.
6. Existing CLI/worker commands must continue to work.

---

## Dependency Injection

Register all new abstractions in the existing DI layer.

Expected registrations:

```csharp
services.AddScoped<IProductUrlDiscoveryStrategyFactory, ProductUrlDiscoveryStrategyFactory>();
services.AddScoped<CategorySeedProductUrlDiscoveryStrategy>();
services.AddScoped<ICategorySeedProvider, JsonCategorySeedProvider>();
services.AddScoped<ICategoryPageLoader, CategoryPageLoader>();
services.AddScoped<ICategoryProductLinkExtractor, CategoryProductLinkExtractor>();
services.AddScoped<ICategoryPaginationStrategy, CategoryPaginationStrategy>();
```

Adjust lifetimes to match current project conventions.

Avoid service locator style if possible.

---

## Configuration

Expected config keys:

```text
Crawler:DiscoveryMode
Crawler:CategorySeedUrls
Crawler:CategorySeedUrlsFilePath
Crawler:MaxCategoryPagesPerSeed
```

Use existing config keys where already present.

Do not introduce duplicate settings if equivalent ones already exist.

If `category-seed-urls.varus.json` is already loaded by the project, reuse that loading mechanism.

If not, implement a dedicated JSON seed provider.

---

## Tests

Add or update tests for the sections below.

### 1. Strategy Factory

Verify:

- Missing `Crawler:DiscoveryMode` selects `CategorySeeds`.
- `CategorySeeds` selects `CategorySeedProductUrlDiscoveryStrategy`.
- Unsupported mode throws a clear exception.
- Optional: `Sitemap` selects sitemap strategy only if sitemap strategy exists.

### 2. Category Seed Provider

Verify:

- Reads `category-seed-urls.varus.json`.
- Parses category name and URL.
- Ignores or rejects invalid URLs according to existing project validation rules.
- Throws clear error if the file is missing or malformed.

### 3. Category Discovery Strategy

Verify:

- Loads seed pages.
- Extracts product URLs.
- Deduplicates product URLs.
- Applies pagination.
- Stops on no next page.
- Stops on no new product URLs.
- Stops on `MaxCategoryPagesPerSeed`.

### 4. Logging-Sensitive Behavior

No need to assert every log message unless the project already has logging test helpers.

But the strategy must expose enough internal behavior through tests to prove pagination and stop reasons work.

---

## Non-Goals

Do not implement the real Varus API strategy yet.

Do not remove category-seed discovery.

Do not make XML sitemap mandatory.

Do not rewrite the whole crawler pipeline.

Do not add proxy rotation, browser automation, Playwright, Selenium, or anti-bot logic in this task.

Do not change database schema unless absolutely required.

Do not change product parsing logic unless it is directly coupled to discovery and must be adjusted.

---

## Acceptance Criteria

- `category-seed-urls.varus.json` is the default phase-1 source for Varus product URL discovery.
- Discovery mode is configurable via `Crawler:DiscoveryMode`.
- Category discovery is represented as a strategy.
- Strategy selection is done through a factory.
- Category discovery logic is split into separate components.
- No single class owns seed reading, HTTP loading, parsing, pagination, filtering, and orchestration at the same time.
- Category pagination is bounded by `Crawler:MaxCategoryPagesPerSeed`.
- Discovery does not depend on XML sitemap being available.
- Existing worker flow still runs.
- Existing tests pass.
- New tests cover factory selection, seed loading, category discovery, deduplication, and pagination stop conditions.
- Names and logs clearly show that category seed discovery is primary, not fallback.

---

## Suggested Implementation Order

1. Inspect current `MPC-59` merged implementation.
2. Identify the current class or classes responsible for category product URL discovery.
3. Introduce `IProductUrlDiscoveryStrategy`.
4. Introduce `CategorySeedProductUrlDiscoveryStrategy`.
5. Extract seed loading into `ICategorySeedProvider`.
6. Extract page loading into `ICategoryPageLoader`.
7. Extract product link parsing into `ICategoryProductLinkExtractor`.
8. Extract pagination into `ICategoryPaginationStrategy`.
9. Introduce `IProductUrlDiscoveryStrategyFactory`.
10. Wire everything through DI.
11. Add `Crawler:DiscoveryMode` and `Crawler:MaxCategoryPagesPerSeed`.
12. Update tests.
13. Run full test suite.
14. Update README or crawler docs with the new discovery modes.

---

## Test Commands

Run:

```bash
dotnet restore
dotnet build
dotnet test
```

If the project has a worker smoke command, also run the existing once/job command, for example:

```bash
dotnet run --project VarPrice.Worker -- --once --job vegetables
```

Adjust the command if the worker project path or job name differs.

---

## Final Report Expected From Codex

After implementation, report:

1. Files changed.
2. New interfaces/classes added.
3. Existing classes refactored.
4. Config keys added or changed.
5. Tests added.
6. Commands executed.
7. Any behavior intentionally preserved from `MPC-59`.
8. Any remaining risks or TODOs.
