# Architecture

## Layer responsibilities

### VarPrice.Domain
- Core entities: `CrawlerRun`, `IngestionRun`, `Product`, `PriceSnapshot`, `CrawlError`.
- Domain enums/value objects: `RunStatus`, `ErrorInfo`.
- Repository ports: `ICrawlerRunRepository`, `IIngestionRunRepository`, `IPriceCollectQueueRepository`, `IPriceSnapshotRepository`.

### VarPrice.Application
- `RunCrawlerUseCase` orchestrates:
  1. Start `crawler_run`.
  2. Start linked `ingestion_run`.
  3. Collect and filter product urls.
  4. Enqueue urls into `price_collect_queue`.
  5. Reserve queue batches with lease (`FOR UPDATE SKIP LOCKED`).
  6. Extract cards and write idempotent `price_snapshot` / `crawl_error` records.
  7. Retry transient failures with backoff; move exhausted items to dead-letter state.
  8. Run reaper for expired leases.
  9. Finalize statuses when queue is drained.
- On failure: ingestion receives `ErrorInfo`; crawler run is marked `Error`.

### VarPrice.Infrastructure
- `PgCrawlerRunRepository`, `PgIngestionRunRepository`, `PgPriceSnapshotRepository`.
- All write-side business operations now execute through DB routines instead of inline DML.
- `crawler_run`, `ingestion_run`, and `crawl_error` are persisted through dedicated domain routines.
- `PgPriceSnapshotRepository.StoreObservationAsync` calls `price_observation_store`, which performs product lookup/upsert,
  latest snapshot read, meaningful-change detection, conditional `price_snapshot` insert, and returns the write result.
- `PgPriceCollectQueueRepository` executes queue enqueue/reserve/retry/dead/reap/stats through DB routines,
  preserving `FOR UPDATE SKIP LOCKED`, lease handling, and queue statistics semantics.
- `SchemaBootstrapper` ensures required tables/indexes, applies versioned SQL routine scripts from `db/routines`,
  tracks them in `db_routine_script`, and migrates legacy tables into the normalized schema.
- `PgRoutineExecutor` provides reusable function/procedure invocation helpers for future write-side DB routines.
- HTTP adapters: `SitemapReader`, `VarusProductCardExtractor`.
- Composition root extension: `AddVarPriceInfrastructure(configuration)`.

### VarPrice.Web
- MVC dashboard uses query sources and triggers `RunCrawlerUseCase`.
- No direct write-side DB access from the UI layer.
- Read-side data for grids is served through dedicated query sources over EF Core.
- The product analytics panel is aggregated through `IProductAnalysisService`, which returns a unified payload for
  product card, history, and chart analytics by `snapshotId`.
- Manual live product refresh reuses `IProductCardExtractor` explicitly from the web layer, but stays read-only and does not persist a new snapshot by itself.

### VarPrice.Worker
- Standalone console runner.
- Parses CLI args and invokes `RunCrawlerUseCase`.
- No web host required.

## Verification

- `VarPrice.Web.Tests/WorkerIntegrationTests` covers the key DB routine flows:
  runs start/finish, observation writes, crawl errors, queue lifecycle, reaper, stats, and end-to-end crawler execution.

## Composition

Both executable apps use:

- `AddVarPriceApplication(configuration)`
- `AddVarPriceInfrastructure(configuration)`

This keeps workflow/business logic reusable for future UI replacements (desktop/other hosts).
