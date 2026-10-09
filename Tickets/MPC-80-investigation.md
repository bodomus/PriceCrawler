# MPC-80 — investigation

## Workflow

- Level: 2 — database/startup/environment operational change.
- Base branch and commit: `Codex/MPC-79`, `4cb787fcb18af58f27f1aa55bfd1cc3e4c588a28`.
- Working tree before MPC-80 changes: clean.
- Graphify: rebuilt after the MPC-79 commit; 242 source files, 1,990 nodes, 4,821 edges.
- CRG: stale metadata was detected, so a full rebuild was started. The structural graph completed on `Codex/MPC-80` / `4cb787f` with 98,083 nodes, 568,053 edges, and 272 files. Expensive flow/community post-processing did not terminate within the five-minute command limit and was stopped after the structural database was safely written. A subsequent incremental update completed successfully. CRG MCP transport was unavailable after the rebuild process; Graphify, `rg`, direct source inspection, SQL inspection, and tests remain authoritative.

## Current behavior

`PriceCrawler.Web` and `PriceCrawler.Worker` both resolve `DatabaseSchemaStartupService` before `app.Run()` or any Worker use case. The service uses two booleans:

- `AllowAutomaticInitialization`;
- `ValidateOnStartup`.

When automatic initialization is enabled, the service calls `SchemaBootstrapper.EnsureSchemaAsync()`. For Stage, Staging, and Production it silently skips the requested mutation and continues to validation. The protected-environment check is embedded in the service. Configuration therefore expresses an unsafe request without failing fast, and there is no explicit operational mode in logs or options.

`SchemaBootstrapper` is highly mutating: it can rename legacy tables, execute `schema.sql`, create/replace routines, update routine hashes, migrate application data, and drop legacy tables. It emits the generic `Schema ensured` message.

`DatabaseSchemaVersionReader` is read-only. It executes only:

```sql
select to_regclass('public.schema_version') is not null;
select max(version) from public.schema_version;
```

The expected schema version is correctly centralized in `DatabaseSchema.ExpectedVersion`.

## Expected behavior

- One explicit `DatabaseSchemaStartupMode`: `Ensure` or `ValidateOnly`.
- Development and Test configuration defaults to `Ensure`.
- Stage, Staging, and Production configuration defaults to `ValidateOnly`.
- `Ensure` is permitted only for Development and Test.
- Any protected/unknown environment configured as `Ensure` aborts before opening a database connection or invoking an initializer.
- `ValidateOnly` calls only the read-only validator.
- Web does not listen and Worker does not resolve/run a crawler use case before successful validation.
- Empty Development/Test databases initialize from `0001_baseline.sql`, then validate schema version 1.
- Existing Development/Test databases may use the approved legacy ensure path, then validate.
- Missing, older, or newer metadata fails with an actionable secret-free error.

## Root cause / missing capability

MPC-79 introduced version metadata and a shared service, but intentionally left the existing boolean initialization control in place. MPC-80 must turn this into an explicit policy and separate initialization from validation. The current service also cannot initialize a truly empty database to a versioned state because `SchemaBootstrapper` does not create `schema_version`; the versioned baseline is the correct empty-database path.

## Affected boundaries

- Infrastructure: schema options, policy, coordinator, initializer, validator, version reader, DI registration, SQL asset resolution, bootstrapper logging.
- Web: startup coordinator call and environment configuration.
- Worker: startup coordinator call, command-start logging order, environment configuration, baseline asset packaging.
- Tests: real PostgreSQL startup behavior, no-DDL role validation, configuration precedence/guard, Web port gating, Worker work gating.
- Documentation: `README.md`, `db/README.md`, `docs/database-environments.md`, `docs/architecture.md`, `scripts/howdeploy.md`, `Status.md`, `CHANGELOG.md`.

## Database and data impact

- No schema version 2 and no new migration.
- No Stage or Production database is contacted or modified during implementation.
- `ValidateOnly` must not begin a transaction, execute DDL/DML, baseline, bootstrap, migration, or repair.
- Development/Test `Ensure` may mutate schema only through the explicit initializer.
- Baseline remains forward-only; downgrade remains unsupported.

## Expected blast radius

Direct:

- `PriceCrawler.Infrastructure.Persistence` schema-startup types;
- DI registration;
- Web/Worker `Program.cs`;
- environment JSON files and host project content assets;
- schema startup tests.

Adjacent:

- direct test-only `SchemaBootstrapper` calls remain supported;
- release packaging already includes database assets and schema version metadata;
- business repositories, queue logic, crawler concurrency, and application data contracts are unchanged.

## Validation required

- Configuration mapping and safety-guard unit tests.
- PostgreSQL integration tests for Ensure, ValidateOnly, mismatch cases, unchanged object/row counts, and a role without DDL permission.
- Process tests proving failed Stage Web does not listen and failed Stage Worker creates no run/work data.
- Existing `WorkerIntegrationTests`.
- Release build and full solution tests.
- Development Web smoke and controlled Stage/Production validation smoke against isolated databases only.
- Post-change CRG update and Graphify refresh because startup orchestration changes structurally.

