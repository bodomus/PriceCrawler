# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## [Unreleased]
### Added
- MPC-96: per-store category tree. Schema version 2 (`db/migrations/0002_category_tree.sql`) adds `category` (tree rows keyed by `source` + `external_id`, never deleted: vanished categories are deactivated, moved ones re-parented) and `product_catalog_category` (which catalog items a category page listed). `catalog-refresh` reads the tree from the varus.ua category menu (`Crawler:CategorySeedSource=Tree`, `CategoryTreeUrl`, `CategoryTreeMinimumNodes`), crawls only leaf categories to their last page and links products to them; it falls back to the seed file when the tree is unavailable. Category steps never fail the refresh, and a tree less than half the stored one is not synced. `ICategoryRepository.GetChildrenAsync` returns one tree level with descendant product counts for the future API.
- MPC-96: first forward migration. Development/Test `Ensure` applies pending `db/migrations/NNNN_*.sql` after the baseline/legacy path; Web and Worker ship all migrations. `Scripts/apply-collector-migrations.ps1` backs up `varprice` and applies pending migrations for the release collector; `publish-collector.ps1` refuses to publish while the working database is behind the release schema (`-SkipSchemaCheck` to bypass).
- MPC-91: daily collection through the Windows Task Scheduler. `Scripts/register-scheduled-task.ps1` registers `PriceCrawler Daily Collect` (08:00, hourly retries until 22:00, start when available after a missed run, no parallel instances) and removes the broken legacy `\Magazine Crawler` task. `scheduled-run.ps1 -Scheduled` skips a start when today's scheduled run already succeeded (marker `state\last-scheduled-success.txt`), so an interrupted run (power cut) is resumed by the next retry; manual runs stay unlimited. `publish-collector.ps1` copies the scheduled scripts to `J:\Apps\PriceCrawler\scripts\`.

### Changed
- MPC-97: all projects target `net10.0` with C# 14 (.NET 8 support ends 10.11.2026); SDK pinned to 10.0.100 in `global.json` and CI. Packages updated to their .NET 10 lines (EF Core / Microsoft.Extensions 10.0.14, Npgsql 10.0.3, Serilog 10.x, xunit 2.9.3, Test SDK 18.11). Redundant `Microsoft.Extensions.*` references removed from Application (NU1510). `publish-collector.ps1` refuses to publish when the .NET 10 runtimes are not installed on the collector machine. Local builds now go to `bin\<Configuration>\net10.0`.
- MPC-96: `Crawler:MaxCategoryPagesPerSeed` 10 -> 30 (leaf categories are read to their last page).
- MPC-90: development runs (Web, Worker from the repository) use `varprice_stage` instead of the working database `varprice`; Web and Worker log their database target at startup. New `Scripts/refresh-stage-from-backup.ps1` (`refresh_stage.bat`) replaces `varprice_stage` with the newest `varprice` backup, refuses any other target and asks for confirmation. Database roles documented in `docs/database-environments.md` (1.1). The destructive debug seed `db/seeds/001__local_debug_month.sql` is now guarded for `varprice_stage` instead of `varprice`.

### Fixed
- MPC-94: a successfully checked catalog item is now due again at the start of the next local day (`Crawler:AlignSuccessfulCheckToDayStart`, default `true`; `Crawler:ScheduleTimeZoneId`) instead of exactly 24 h later. Before, the due window drifted to the time of the previous run, so a morning scheduled run found 0 items and marked the day as done (03.11, run 224).
- MPC-91: `register-scheduled-task.ps1` warns instead of failing when the legacy `\Magazine Crawler` task cannot be removed without elevation (the new task is already registered).
- MPC-89: `publish-collector.ps1` no longer fails at the cleanup step on the first publish (Windows PowerShell 5.1 StrictMode `.Count` on a single value).

### Added
- MPC-89: separate Release collector. `Scripts/publish-collector.ps1` publishes the Worker to `J:\Apps\PriceCrawler\worker\<version>` with local settings outside git, a `current.txt` pointer, rollback and version retention; `Scripts/scheduled-run.ps1` runs Docker → PostgreSQL → Worker (Production, ValidateOnly; `catalog-refresh` on Sundays) → backup → per-run log with stage exit codes; `Scripts/backup-database.ps1` makes a verified, timestamped backup of `varprice` by container name with 14-day/13-week rotation (`backup_varus.bat` now calls it; `restore_varus.bat` uses the container name). See `docs/collector.md`.
- Local Development run for Web and Worker: Worker launch profiles and `run-dev.bat` set `DOTNET_ENVIRONMENT=Development`; optional git-ignored `appsettings.Local.json` (Development only, never published) for personal debug overrides instead of editing files in `bin/`.
- MPC-84: Stage-approved Production deployment with strict Production identity/independence guards, mandatory verified backup, forward-only migrations, Production-only runtime grants, immutable release activation, Web health gating, Worker stabilization, dry-run, recovery evidence, and JSON reporting.
- MPC-83: guarded Stage deployment with package/hash validation, verified backup, explicit Development refresh, forward migrations, Stage-only runtime grants, safe `current` switching, Web health gating, Worker stabilization, dry-run, logs, and JSON reports.
- Validated release-package contract with canonical NBGV/Git metadata, ordered migration inventory, safe Web/Crawler configuration staging, deterministic ZIP entry ordering, SHA-256 sidecar, forbidden-content checks, and behavioral packaging tests.
- Separate non-superuser Stage/Production Web and Worker runtime-role provisioning with external secret inputs, least-privilege data/routine grants, active DDL-denial probes, and `ValidateOnly` host-start integration coverage.
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
- MPC-93: `collect-prices` and `vegetables` no longer fail the whole run because of single dead queue items. The run is `error` only when the share of dead items exceeds `Crawler:RunErrorDeadThresholdPercent` (default 1%) or the process fails; below the threshold it is `ok` with a warning. Worker exit codes are unchanged.
- Release artifacts now default to `artifacts/releases`, refuse silent replacement, expose explicit output/replacement controls, and validate both the staging tree and final archive against schema/component metadata.
- Partial Stage/Production bootstrap failures now retain the verified Development dump and print an explicit operator recovery procedure; failed initial Production databases are never deleted automatically and may be retried only after marker/history checks.
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

### Security
- Upgraded AngleSharp 1.1.2 → 1.8.5 to resolve GHSA-pgww-w46g-26qg (CVE-2026-54570, mXSS in HTML serialization), which failed Release restore with NU1902 as error.

### Fixed
- CI: tests that execute Windows PowerShell deployment/release scripts are tagged `Platform=Windows` and excluded on the Linux runner (`--filter "Platform!=Windows"`); they still run locally on Windows. Fixed `scripts` → `Scripts` path casing in script contract tests so they pass on case-sensitive file systems.
- VARUS listing/category product discovery now uses server-rendered JSON-LD Product ItemList
  records instead of falling back to every page anchor, preventing navigation and service URLs
  from inflating discovery and queue progress metrics.
- Removed stale documentation assumptions about `city`, `product_errors`, `discount_percent`, and `last_seen_at`.
- Worker positional commands are no longer forwarded to Generic Host configuration parsing; Worker configuration is loaded from the executable content root, and `--once` is rejected outside `vegetables`.
