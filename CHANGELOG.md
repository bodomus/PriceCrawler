# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## [Unreleased]
### Added
- `crawl_error` table and domain models for normalized crawler error persistence.
- Documentation for the normalized `product` / `price_snapshot` schema introduced by `MPC-21`.
- `ProductAnalytics` payload and real dashboard chart backed by Postgres history.
- Unified `ProductAnalysis` dashboard payload for product card, history, and chart analytics.
- Manual `RefreshLiveProduct` dashboard action for explicit live VARUS checks without automatic DB writes.
- Versioned `db/routines` catalog with bootstrap support for separate SQL routine scripts.
- `PgRoutineExecutor` and `DbRoutineCall` helpers for calling PostgreSQL functions/procedures from write-side C# code.
- Integration coverage for write-side DB routines in `WorkerIntegrationTests`.

### Changed
- Database schema refactored around internal `product.id` links instead of legacy `product_key`.
- `price_snapshot` now stores `price` / `old_price` and acts as the fact table for product observations.
- Queue/pipeline, repositories, parser output, dashboard queries, and tests were aligned with the new storage model.
- `/Runs` dashboard now combines Postgres analytics with an explicit live comparison action on the product card.
- `/Runs` analytics panel now loads through a single application-level `ProductAnalysis` contract instead of multiple unrelated fetches.
- `SchemaBootstrapper` now applies `schema.sql` together with tracked `db/routines/*.sql` scripts and the app hosts
  ship those SQL assets in their output/publish directories.
- `crawler_run`, `ingestion_run`, and `crawl_error` write-side persistence now goes through DB routines instead of inline SQL DML in the repositories.
- `price_collect_queue` lifecycle operations now go through DB routines as well, including enqueue, reserve with `FOR UPDATE SKIP LOCKED`, retry/dead transitions, reaper, outstanding checks, and run stats.
- `StoreObservationAsync` now executes as a single DB-side business operation through `price_observation_store`, covering product lookup/upsert, snapshot comparison, conditional insert, and write result return.

### Fixed
- Removed stale documentation assumptions about `city`, `product_errors`, `discount_percent`, and `last_seen_at`.
