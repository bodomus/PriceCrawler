# MPC-73 — Progress UI queue metrics

## Summary

Fixed Worker console progress metrics so discovery/catalog counters are not mixed with queue processing counters.

## What changed

- Added explicit `QueueLinksRequested` progress counter.
- Reset progress state at the start of each crawler use case to avoid stale counters in sequential runs such as `run-all`.
- `collect-prices` now reports initially enqueued links through queue metrics instead of `NewProducts`.
- Listing/filter queue items now increment the requested queue link total when they discover and enqueue product URLs.
- Console dashboard now shows:
  - discovered
  - new
  - updated
  - selected for check
  - links in queue
  - processed links
  - successful
  - errors
  - current stage
  - current link
  - completion percent
- README was updated with the new console progress semantics.

## Validation

- `dotnet build VarPrice.sln`
- `dotnet test VarPrice.Web.Tests\VarPrice.Web.Tests.csproj --filter CrawlerProgressStateTests`
- `dotnet test VarPrice.sln`

All tests passed. Integration tests use `varprice_test` through `PostgresIntegrationFixture`.

## Database

No schema or data changes were made. The working `varprice` database was not changed.
