# MPC-70 — Worker progress dashboard for active commands

## Summary

Fixed Worker progress dashboard wiring for the active CLI commands:

- `catalog-refresh`
- `collect-prices`
- `run-all`
- legacy/default `vegetables`

The dashboard is now started through a shared Worker wrapper and stopped in a `finally` block for every interactive crawler command. Invalid commands and `--help` still return before host/dashboard startup.

## Changes

- Added shared dashboard execution wrapper in `PriceCrawler.Worker/Program.cs`.
- Added dashboard disabled diagnostics for redirected stdout and too-small consoles.
- Preserved file Serilog sink path and output template.
- Added `ICrawlerProgressReporter` updates to `RefreshProductCatalogUseCase`:
  - discovery stage;
  - total discovered;
  - catalog update stage;
  - inserted/updated counters;
  - completed/error terminal stage.
- Added `ICrawlerProgressReporter` updates to `CollectProductPricesUseCase`:
  - selection stage;
  - selected/total counters;
  - enqueued counter;
  - price checking stage;
  - completed/error terminal stage.
- Kept existing `PriceCollectionQueueProcessor` item-level progress behavior for current item, checked, successful, and failed counters.
- Added focused progress reporter tests for catalog refresh and price collection.
- Updated README to document the dashboard for `vegetables`, `catalog-refresh`, `collect-prices`, and `run-all`.

## Validation

All commands were run with `TEMP` and `TMP` set to `A:\` and build/test artifacts redirected to `A:\`.

```powershell
dotnet test PriceCrawler.Web.Tests\PriceCrawler.Web.Tests.csproj --filter "FullyQualifiedName~RefreshProductCatalogUseCaseTests|FullyQualifiedName~CollectProductPricesUseCaseTests" --artifacts-path A:\pricecrawler-mpc70-artifacts
```

Result: passed, 28 tests.

```powershell
dotnet test PriceCrawler.Worker.Tests\PriceCrawler.Worker.Tests.csproj --artifacts-path A:\pricecrawler-mpc70-worker-artifacts
```

Result: passed, 21 tests.

```powershell
dotnet build PriceCrawler.sln --artifacts-path A:\pricecrawler-mpc70-build-artifacts
```

Result: build succeeded, 0 warnings, 0 errors.

## Notes

- No database schema changes were made.
- No writes were made to the working `varprice` database.
- A running `PriceCrawler.Worker` process was holding default `bin\Debug` outputs, so validation used `--artifacts-path` on `A:\` instead of stopping that process.
