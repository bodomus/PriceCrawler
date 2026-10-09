# MPC-79 Investigation

## Ticket

Introduce database schema versioning and baseline migration for schema version `1`, associated with application release `v0.4.1-alpha`.

## Workflow classification

- Level: 2 (database schema, startup, environment safety, and release packaging).
- Initial working tree: dirty with user-owned changes in `.agents/skills/PRE_TICKET_WORKFLOW.md`, `.graphifyignore`, `agents.md`, and `docs/database-environments.md`.
- Graphify: refreshed successfully after `.graphifyignore` was updated; 228 source files were processed.
- CRG: current graph contains 98,022 nodes, 567,901 edges, and 261 files. Incremental update completed for the current branch. The CLI printed its result but then hit a Windows CP1251 rendering error in the optional summary panel. The CRG MCP transport became unavailable after a stale writer process was removed, so exact implementation findings below were verified through Graphify, `rg`, source, SQL, PostgreSQL catalogs, builds, and tests.

## Current architecture and behavior

- `schema.sql` defines the current table, constraint, and index creation/upgrade SQL.
- `db/routines/*.sql` defines six ordered routine scripts. `DbRoutineScriptCatalog` hashes them and `SchemaBootstrapper` reapplies a routine when its stored hash changes.
- `SchemaBootstrapper.EnsureSchemaAsync` performs legacy table renames, executes `schema.sql`, applies routines, migrates legacy data, and drops legacy tables in one transaction after opening the database connection.
- Both `PriceCrawler.Web/Program.cs` and `PriceCrawler.Worker/Program.cs` unconditionally call `SchemaBootstrapper.EnsureSchemaAsync` at startup.
- Consequently, Development, Stage, and Production currently share the same mutation-capable startup path. There is no schema-version contract or compatibility validation.
- PostgreSQL integration tests also call `SchemaBootstrapper` and then truncate shared Test tables.
- `scripts/build-release.ps1` currently packages only `web`, `crawler`, and basic `release.json` metadata.

## Expected behavior

- A self-contained immutable `db/migrations/0001_baseline.sql` creates a clean database and registers schema version `1`.
- A separate validation-first `db/scripts/bootstrap-schema-version.sql` registers a structurally compatible existing database without rebuilding or changing application objects or data.
- Web and Worker consume one Infrastructure-owned schema contract and startup service.
- Stage and Production startup are validation-only and fail fast for missing, older, or newer schema metadata.
- Development/Test automatic legacy initialization remains possible only when explicitly configured, followed by version validation.
- The release archive contains database deployment files and schema version metadata.

## Development database evidence

Database inspected: local Docker PostgreSQL 16 database `varprice` in `var_postgres`.

- PostgreSQL version: 16.10.
- Public base tables: 11.
- Public views/materialized views: 0.
- Public routines reported by `information_schema`: 41.
- `schema_version`: missing.
- Schema-only analysis dump: `artifacts/db/development-schema.sql` (generated artifact, not source).
- All six `db_routine_script` SHA-256 values match the current repository files.

An isolated Test database was created from `schema.sql` plus all six routine scripts:

- Public base tables: 10.
- Public routines: 40.
- Columns and constraints match Development except for explicitly listed additional Development objects.

## Reconciled discrepancies

1. Development contains `__EFMigrationsHistory`, but the current application does not execute EF Core migrations. This table and its index are unrelated legacy metadata. They are excluded from the baseline and accepted as non-blocking additional objects by bootstrap.
2. Development contains the obsolete overload `product_catalog_upsert_discovered(p_items text)`. Current repository SQL and C# use the newer overload with `p_refresh_id`. The old overload is excluded from the baseline and accepted as a non-blocking additional routine.
3. Development `ix_product_catalog_due` is `(is_active, next_check_at, last_checked_at, id)`, while current `schema.sql` defines `(is_active, next_check_at, reserved_until, last_checked_at, id)`. The active `product_catalog_get_due` routine filters `reserved_until`; therefore the repository definition is the intended current structure. Before Development is registered as version `1`, create a Development backup and explicitly rebuild this index. Bootstrap itself must only validate and must not change the index.

## Ownership and smallest coherent change

Schema-version contracts, options, reading, validation, startup orchestration, and exceptions belong to `PriceCrawler.Infrastructure/Persistence` because they are PostgreSQL-specific and already shared by Web and Worker through Infrastructure DI.

The smallest coherent change is:

1. Add the immutable baseline and non-destructive bootstrap SQL.
2. Add one shared expected-version contract, structured result, reader, validator/startup service, and strongly typed options.
3. Replace direct Web/Worker `SchemaBootstrapper` calls with the shared startup service.
4. Enforce validation-only behavior for Stage/Staging and Production in code, regardless of an unsafe automatic-initialization configuration value.
5. Add real PostgreSQL tests for baseline, bootstrap, mismatch, and no-mutation behavior.
6. Package database files and version metadata.
7. Reconcile and bootstrap the local Development database only after backup and successful isolated validation.

## Expected blast radius

- Direct: Infrastructure persistence and DI, Web startup/configuration, Worker startup/configuration, SQL assets, PostgreSQL integration tests, release build script.
- Adjacent: project content-copy rules, README/Status/changelog, database environment documentation.
- No intended crawler/queue behavior change, request-rate change, public HTTP contract change, or Worker command/exit-code change.
- Database impact: one new metadata table/row; one explicit Development-only index reconciliation before registration; no Stage/Production startup DDL.
- Deployment impact: operators must apply baseline/bootstrap/migrations before starting Stage or Production.

## Safety conclusions

- No downgrade path will be introduced.
- Baseline is only for an empty database and must fail on unexpected public objects.
- Bootstrap permits extra non-conflicting objects but rejects missing/wrong required tables, columns, keys, indexes, routines, and conflicting version metadata.
- Production bootstrap is guarded by database-name detection and requires an explicit session override after the required backup process.
- Application errors must never include connection strings or credentials.

