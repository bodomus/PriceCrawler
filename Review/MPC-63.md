# MPC-63 — CollectProductPricesUseCase and oldest-first catalog selection

## Summary

Implemented a new daily price collection flow that reads due products from `product_catalog`, reserves them oldest-first, enqueues them into `price_collect_queue`, processes them through the existing extractor/snapshot pipeline, and updates catalog scheduling state after final success or final failure.

## Runtime flow

```text
crawler_run_start
-> ingestion_run_start
-> product_catalog oldest-first reservation
-> price_collect_queue enqueue with product_catalog_id
-> queue reserve/drain
-> extractor
-> price_observation_store / crawl_error
-> product_catalog mark checked or failed
-> ingestion_run_finish
-> crawler_run_finish
```

## Database changes

- Added `product_catalog.reserved_at`, `reserved_until`, `reserved_by`.
- Added `price_collect_queue.product_catalog_id`.
- Added FK `price_collect_queue.product_catalog_id -> product_catalog(id)` with `on delete restrict`.
- Added indexes for catalog reservation/due selection and queue catalog linkage.
- Added `product_catalog_get_due`, `product_catalog_mark_checked`, `product_catalog_mark_failed`.
- Updated queue enqueue/reserve routines to carry `product_catalog_id`.

## Code changes

- Added `ICollectProductPricesUseCase`, `CollectProductPricesUseCase`, `CollectProductPricesResult`.
- Added `PriceCollectionQueueProcessor` shared by legacy `RunCrawlerUseCase` and new catalog collection flow.
- Added `ProductCatalogRetryPolicy`.
- Extended `IProductCatalogRepository` / `PgProductCatalogRepository`.
- Added worker command `--collect-prices` / `collect-prices`.
- Added crawler config keys for catalog lease, success interval, and failure backoff.

## Validation

- `dotnet restore`: completed; NU1900 warning because NuGet vulnerability metadata endpoint was unavailable.
- `dotnet build --no-restore`: passed; same NU1900 warnings.
- `dotnet test --no-build`: passed, 161 tests.
- Targeted tests passed:
  - `CollectProductPricesUseCaseTests`
  - `ProductCatalogRepositoryTests`
  - `RunCrawlerUseCaseTests`
- Manual smoke on `varprice_test` with stub extractor:
  - `RunId`: 1
  - `Selected`: 2
  - `Enqueued`: 2
  - `Succeeded`: 2
  - `Retry`: 0
  - `Dead`: 0
  - `Snapshots created`: 2
  - `Catalog rows updated`: 2

## Notes

- All DB validation and smoke checks were run against `varprice_test`.
- A small real-extractor run was not executed to avoid making live Varus requests during this pass.
- Deactivation of missing catalog products and a full scheduler remain future work.
