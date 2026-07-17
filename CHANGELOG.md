# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## [Unreleased]
### Added
- Repeatable Test/Stage/Production provisioning via `scripts/initialize-database-environments.ps1`, including Docker/native PostgreSQL tooling, `-WhatIf`, guarded replacement, verified logical dumps, SHA-256 checksums, sanitized logs, and bootstrap reporting.
- One-time Production bootstrap protection through a durable database-level independence marker, initial verified backup, and permanent refusal of Development-to-Production overwrite.
- Safe four-environment connection-string templates and PostgreSQL integration coverage for Test baseline policy, Stage snapshot restore, Production bootstrap, and second-attempt rejection.
- Explicit `DatabaseSchemaStartupMode` (`Ensure` / `ValidateOnly`), separated initializer/validator/coordinator services, and a non-bypassable environment safety policy.
- PostgreSQL and process-level coverage proving Stage/Production validation is read-only, works without DDL permission, blocks Web listening, and blocks Worker processing on failure.
- Explicit database schema version `1`, immutable `0001_baseline.sql`, and validation-first existing-database bootstrap.
- Shared Web/Worker schema compatibility reader and startup validation with protected Stage/Production behavior.
- Database deployment assets and minimum/target schema metadata in release packages.
- `crawl_error` table and domain models for normalized crawler error persistence.
- Documentation for the normalized `product` / `price_snapshot` schema introduced by `MPC-21`.
- `ProductAnalytics` payload and real dashboard chart backed by Postgres history.
- Unified `ProductAnalysis` dashboard payload for product card, history, and chart analytics.
- Manual `RefreshLiveProduct` dashboard action for explicit live VARUS checks without automatic DB writes.
- Versioned `db/routines` catalog with bootstrap support for separate SQL routine scripts.
- `PgRoutineExecutor` and `DbRoutineCall` helpers for calling PostgreSQL functions/procedures from write-side C# code.
- Integration coverage for write-side DB routines in `WorkerIntegrationTests`.
- Local/dev SQL seed script `db/seeds/001__local_debug_month.sql` for destructive reset + generation of realistic month-long debug data.
- Explicit Worker CLI commands: `vegetables`, `catalog-refresh`, `collect-prices`, and `--help`.

### Changed
- Web and Worker now use one shared schema startup coordinator. Development/Test explicitly `Ensure`; Stage/Staging/Production explicitly `ValidateOnly` and reject unsafe overrides before database access.
- Empty Development/Test databases initialize from `0001_baseline.sql`; repeated Test initialization restores connection session state and remains deterministic.
- Generic `Schema ensured` logging was replaced with structured environment, mode, expected/actual version, result, and failure-reason fields.
- PostgreSQL integration coverage now creates isolated databases from the canonical baseline and verifies bootstrap mismatch/repeat behavior.
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
- Worker argument parsing now validates unsupported options and conflicting modes before host creation and DB bootstrap.

### Fixed
- VARUS listing/category product discovery now uses server-rendered JSON-LD Product ItemList
  records instead of falling back to every page anchor, preventing navigation and service URLs
  from inflating discovery and queue progress metrics.
- Removed stale documentation assumptions about `city`, `product_errors`, `discount_percent`, and `last_seen_at`.
- Worker positional commands are no longer forwarded to Generic Host configuration parsing; Worker configuration is loaded from the executable content root, and `--once` is rejected outside `vegetables`.
