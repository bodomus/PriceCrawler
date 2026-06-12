# Architecture

## Layer responsibilities

### VarPrice.Domain
- Core entities: `CrawlerRun`, `IngestionRun`, `Product`, `ProductCatalogItem`, `PriceSnapshot`, `CrawlError`.
- Domain enums/value objects: `RunStatus`, `ErrorInfo`.
- Repository ports: `ICrawlerRunRepository`, `IIngestionRunRepository`, `IPriceCollectQueueRepository`,
  `IPriceSnapshotRepository`, `IProductCatalogRepository`.

### VarPrice.Application
- `RunCrawlerUseCase` orchestrates:
  1. Discover and filter product urls.
  2. Start `crawler_run`.
  3. Start linked `ingestion_run`.
  4. Enqueue up to `Crawler:MaxProductsPerRun` urls into `price_collect_queue`.
  5. Reserve queue batches with lease (`FOR UPDATE SKIP LOCKED`).
  6. Extract cards and write idempotent `price_snapshot` / `crawl_error` records.
  7. Retry transient failures with backoff; move exhausted items to dead-letter state.
  8. Run reaper for expired leases.
  9. Finalize statuses when queue is drained.
- On failure: ingestion receives `ErrorInfo`; crawler run is marked `Error`.
- `RefreshProductCatalogUseCase` orchestrates catalog refresh:
  1. Start `crawler_run` with source `catalog-refresh` before discovery.
  2. Run `IProductUrlDiscoveryService`.
  3. Convert discovered URLs to `ProductCatalogUpsertItem` with product source `varus`.
  4. Call `IProductCatalogRepository.UpsertDiscoveredAsync` once for the batch.
  5. Finish `crawler_run` with aggregated counters.
- Catalog refresh flow:

```text
Catalog refresh
    ↓
IProductUrlDiscoveryService
    ↓
ProductUrlFilter
    ↓
RefreshProductCatalogUseCase
    ↓
IProductCatalogRepository
    ↓
product_catalog
```

- Catalog refresh does not collect prices and does not create `ingestion_run`, `price_collect_queue`,
  `price_snapshot`, or `product` rows.
- `Crawler:MaxUrls` limits discovery/catalog refresh. `Crawler:MaxProductsPerRun` limits only price collection queue
  size.
- Discovery source (`category-seed`, `sitemap`, `api`) is reported in result/logs; catalog item source remains `varus`.

### VarPrice.Infrastructure
- `PgCrawlerRunRepository`, `PgIngestionRunRepository`, `PgPriceSnapshotRepository`, `PgProductCatalogRepository`.
- All write-side business operations now execute through DB routines instead of inline DML.
- `crawler_run`, `ingestion_run`, and `crawl_error` are persisted through dedicated domain routines.
- `product_catalog` stores the persistent discovered product URL catalog. It is distinct from `product`:
  `product_catalog` owns discovery URL state, activity, and future scheduling metadata, while `product` remains the
  normalized product entity created from extracted product cards and linked to `price_snapshot`.
- Runtime catalog refresh now connects discovery to `product_catalog`; price collection still reads discovered URLs
  through the existing crawler flow and does not yet schedule from `product_catalog`.
- `PgPriceSnapshotRepository.StoreObservationAsync` calls `price_observation_store`, which performs product lookup/upsert,
  latest snapshot read, meaningful-change detection, conditional `price_snapshot` insert, and returns the write result.
- `PgProductCatalogRepository.UpsertDiscoveredAsync` prepares discovered URLs in memory, removes invalid/duplicate input,
  and sends the whole batch to `product_catalog_upsert_discovered` in one DB routine call.
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
- Parses CLI args and invokes `RunCrawlerUseCase` for `vegetables` or `RefreshProductCatalogUseCase` for
  `catalog-refresh`.
- No web host required.

## Verification

- `VarPrice.Web.Tests/WorkerIntegrationTests` covers the key DB routine flows:
  runs start/finish, observation writes, crawl errors, queue lifecycle, reaper, stats, and end-to-end crawler execution.
- Catalog refresh integration tests cover `crawler_run`, `product_catalog` insert/update behavior, and absence of price
  queue/snapshot/product/ingestion writes.

## Composition

Both executable apps use:

- `AddVarPriceApplication(configuration)`
- `AddVarPriceInfrastructure(configuration)`

This keeps workflow/business logic reusable for future UI replacements (desktop/other hosts).
