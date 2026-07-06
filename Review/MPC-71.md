# MPC-71 - Live discovery progress in console dashboard

## Summary

Implemented live category seed discovery progress for the Worker console dashboard.

## Changes

- Extended `ICrawlerProgressReporter` and `CrawlerProgressSnapshot` with discovery progress fields:
  - processed/current seed count;
  - total seed count;
  - discovered unique product URL count;
  - current seed name;
  - current seed URL;
  - current page number.
- Updated `CrawlerProgressState` to store discovery progress, update `TotalDiscovered` live, and expose current category/page in the existing dashboard current item line.
- Added no-op compatibility for non-dashboard/non-category flows.
- Injected `ICrawlerProgressReporter` into `CategorySeedProductUrlDiscoveryStrategy`.
- Reported progress before category page load and after each processed page.
- Switched dashboard completion percentage to discovery seed progress while no price-check selection exists yet.
- Cleared stale discovery current item when `RefreshProductCatalogUseCase` moves from discovery to catalog update.
- Added focused tests for discovery progress state and category seed progress reporting.

## Validation

```powershell
dotnet test PriceCrawler.Web.Tests\PriceCrawler.Web.Tests.csproj --filter "FullyQualifiedName~CrawlerProgressStateTests|FullyQualifiedName~ProductUrlDiscoveryTests|FullyQualifiedName~RefreshProductCatalogUseCaseTests"
```

Result: passed, 47 tests.

```powershell
dotnet build PriceCrawler.sln
```

Result: build succeeded, 0 warnings, 0 errors.

## Notes

- No crawler request concurrency, retry, timeout, or throttling behavior was changed.
- No database schema changes were made.
- No writes were made to the working `varprice` database.
