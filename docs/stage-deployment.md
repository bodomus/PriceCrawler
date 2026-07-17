# Stage deployment

`Scripts/deploy-stage.ps1` is the only supported PriceCrawler Stage deployment entry point. It validates an MPC-82 release, backs up Stage, applies forward-only database changes with the deploy identity, activates a versioned release, and gates Worker startup on Web listener ownership and health.

Production is not supported. Production-like Stage or Development database names are rejected before a lock or mutation.

## Prerequisites

- A release ZIP and matching `.zip.sha256` from `Scripts/build-release.ps1`.
- Native `psql`, `pg_dump`, `pg_restore`, `createdb`, and `dropdb`, or an explicitly named running PostgreSQL Docker container.
- A separate deploy/object-owner identity supplied with `-DeployDatabaseUser`.
- Authentication from `.pgpass`, `PGPASSWORD` populated by a secret store, or container secrets. There is no password parameter.
- Existing `varprice_stage`; optional refresh source `varprice`; `varprice_prod` remains outside the workflow.
- External Web and Worker configs targeting `varprice_stage`, using `pricecrawler_stage_web` / `pricecrawler_stage_worker`, with `DatabaseSchema.StartupMode=ValidateOnly`.
- `PRICECRAWLER_STAGE_WEB_DB_PASSWORD` and `PRICECRAWLER_STAGE_WORKER_DB_PASSWORD` from the secret store. Stage-only provisioning reads these values; Production secrets are not required or read.
- Explicit Worker CLI arguments. Worker is a one-shot operational command, not a daemon; deployment starts the chosen real Stage workload, not a synthetic crawler probe.

Recommended external config paths:

```text
stage/config/web/appsettings.Stage.json
stage/config/crawler/appsettings.Stage.json
```

Start from the placeholder-only templates in `config/stage-deployment/web` and `config/stage-deployment/crawler`, copy them outside the release, and resolve every placeholder through the deployment secret store before running deploy. Unresolved templates are deliberately rejected.

Never commit these secret-bearing files or put them in the ZIP.

## Layout

```text
stage/
├── releases/<version>/          immutable package contents
├── current/                     active copy plus external config overlay
├── config/web/
├── config/crawler/
├── logs/                        deploy/Web/Worker logs and JSON reports
├── backups/database/            verified custom-format dumps
├── runtime/                     PID files, deploy.lock, retained evidence
└── deployment-state.json
```

Extraction occurs under `runtime/`; a partial extraction never becomes active. `current.new` is fully prepared and configured before the old `current` is moved aside. The versioned release and its `release.json` remain unchanged.

## Dry run

```powershell
.\Scripts\deploy-stage.ps1 `
    -PackagePath .\artifacts\releases\PriceCrawler-v0.4.1-alpha.zip `
    -StageRoot .\stage `
    -StageDatabase varprice_stage `
    -DevelopmentDatabase varprice `
    -ProductionDatabase varprice_prod `
    -ToolMode Docker `
    -DockerContainer var_postgres `
    -DeployDatabaseUser var `
    -WebUrl http://127.0.0.1:8080 `
    -WebConfigPath .\stage\config\web\appsettings.Stage.json `
    -WorkerConfigPath .\stage\config\crawler\appsettings.Stage.json `
    -WorkerArguments vegetables,--once `
    -WhatIf
```

Dry-run validates package/hash/metadata, config, tools, database existence, and current schema. It prints the backup, refresh, migration, process, current, and health plan but creates no directory, lock, backup, log, report, process, extraction, or database change.

## Normal deployment

Run the same command without `-WhatIf`:

```text
validated package
→ verified Stage backup
→ stop Worker then Web through owned PID records
→ preserve Stage data
→ missing forward migrations only
→ Stage-only runtime grants and DDL-denial probes
→ versioned extraction and external config overlay
→ safe current switch
→ Web → listener ownership → /health
→ Worker stabilization
→ deployment state, log, and JSON report
```

Existing `releases/<version>` is immutable. `-ReplaceExistingRelease` explicitly moves it to retained evidence before replacement; silent overwrite is impossible.

## Explicit Development refresh

Add `-RefreshDatabaseFromDevelopment`. The sequence is: verified Stage backup, stop Worker/Web, verified Development dump, recreate only the configured Stage database, restore, verify schema compatibility, apply missing migrations, and reapply Stage-only runtime grants. Source and destination are explicit and distinct; neither may be Production-like. There is no Development-to-Production path.

## Validation and database rules

Deployment rejects hash mismatch, invalid/duplicate `release.json`, wrong product/commit/timestamp/schema range, missing components/provisioning support, migration gaps/duplicates, traversal/absolute paths, unexpected roots, secrets, `.env`, `.pgpass`, dumps, backups, logs, keys, Graphify/CRG data, and repository artifacts.

Stage backup uses custom format with `--no-owner --no-privileges`; it must be non-empty, pass `pg_restore --list`, and receive SHA-256 before database mutation. Existing `schema_version` is mandatory. Baseline/bootstrap are never executed. Actual schema newer than target fails; equal is a no-op; otherwise only contiguous missing migrations run in ascending order, with version verification after each.

The deploy identity applies migrations. Runtime identities never do. After schema work, the packaged role script runs with `-StageOnly -ExpectedSchemaVersion <target>`, reapplies reviewed allowlists, verifies `ValidateOnly`, and proves `CREATE TABLE`/`ALTER TABLE` denial without inspecting Production.

## Processes and health

Processes are never killed by name. PID records include PID, executable, component root, start time, and version. Executable path and command line must point into the expected Stage root; a stale record is removed only after its PID is confirmed absent.

Web starts with Stage environment, `DatabaseSchema__StartupMode=ValidateOnly`, and explicit `ASPNETCORE_URLS`. `Get-NetTCPConnection` must show the port owned by the started Web PID. Health must return 2xx and not structured `ok=false` before Worker starts. Worker must remain alive through its stabilization interval.

## Logs, reports, and failure recovery

Normal attempts write `stage/logs/deploy-stage-<version>-<timestamp>.log` and `.json`, recording package/version/commit, database names, backup metadata, schema versions, migration hashes, refresh flag, PIDs, health and phase durations. Secrets and secret-bearing connection strings are omitted/redacted.

Failure stops later phases and Worker never starts before health. Newly started processes are stopped; verified backups, logs, reports, failed release evidence, and previous-current evidence remain. The lock is released in `finally`.

Database rollback/restore is never automatic. After a post-switch failure, application rollback is allowed only after confirming the previous application supports the current schema. Never downgrade the schema; use a separately reviewed recovery procedure.
