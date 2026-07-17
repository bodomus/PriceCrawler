# Production deployment

`Scripts/deploy-production.ps1` is the only normal path for deploying PriceCrawler application releases and schema changes to Production. It accepts only an immutable ZIP whose exact SHA-256, version, commit, target schema, Web health, and Worker start are proven by a successful Stage deployment JSON report.

## Prerequisites

- Production was bootstrapped once and its database comment contains `environment=Production` and `initial_bootstrap_completed=true`. Never rerun Production bootstrap.
- `psql`, `pg_dump`, and `pg_restore` are available natively or in the explicitly selected PostgreSQL container.
- the deploy identity authenticates through `PGPASSWORD`, `.pgpass`, or an external secret store and is separate from runtime identities;
- `pricecrawler_prod_web` and `pricecrawler_prod_worker` are non-superuser, have neither `CREATEDB` nor `CREATEROLE`, and receive passwords externally;
- external `appsettings.Production.json` files use their respective runtime identities, `varprice_prod`, and `DatabaseSchema.StartupMode=ValidateOnly`;
- the Production URL/port, health path, and explicit Worker operational arguments are known;
- the exact ZIP, `.sha256` sidecar, and successful Stage report are retained together.

Copy the placeholder templates from `config/production-deployment/` into `production/config/` and resolve them outside Git. Never put a real password or connection string into the package, repository, command line, or deployment log.

## Commands

Dry-run performs read-only package, approval, configuration, database-marker, identity, and schema checks. It creates no Production directories, backup, lock, logs, process changes, extraction, current switch, or database mutation:

```powershell
.\Scripts\deploy-production.ps1 `
    -PackagePath .\artifacts\releases\PriceCrawler-v0.4.1.zip `
    -StageVerificationReportPath .\stage\logs\deploy-stage-v0.4.1.json `
    -ProductionRoot .\production `
    -ProductionDatabase varprice_prod `
    -DeployDatabaseUser pricecrawler_deploy `
    -WebUrl http://127.0.0.1:5000 `
    -WorkerArguments vegetables `
    -ConfirmProductionDeployment `
    -WhatIf
```

Remove `-WhatIf` only after reviewing the plan and Stage evidence. A real deployment is refused without `-ConfirmProductionDeployment`.

## Ordered safety flow

1. Validate ZIP, sidecar, `release.json`, migration inventory, entry points, and forbidden content.
2. Validate the matching successful Stage report; there is no force bypass.
3. Validate the configured Production-only target, independence marker, external configs, separate roles, `ValidateOnly`, deployment lock, and current schema.
4. Create `production/backups/database/<db>-before-<version>-<timestamp>.dump`; verify non-zero size, `pg_restore --list`, and SHA-256.
5. Stop Worker, then Web, using only verified PID records and executable/command-line ownership. Verify the port is released.
6. Apply only missing ordered forward migrations with the deploy identity. A newer schema, gap, duplicate, failure, or mismatch is fatal. No downgrade exists.
7. Reapply reviewed Production-only runtime grants and DDL-denial probes from the package.
8. Extract to a temporary path, validate again, and move to immutable `production/releases/<version>`.
9. Build `current.new` with external Production config and safely move it to `current`, retaining previous-current evidence under `runtime`.
10. Start Web with Production + `ValidateOnly`, verify listener ownership and health, then start Worker and require stabilization.
11. Verify schema/process state, write `deployment-state.json`, text log, and JSON report, then release the lock.

Database backups are never deleted by the deploy script. Retention and off-host copying belong to the operator/DBA policy.

## Failure and recovery

- Package or Stage mismatch: correct the inputs; do not bypass validation.
- Backup failure: no process, database, release, or current mutation has occurred. Fix backup tooling/storage and rerun.
- Migration failure: preserve the verified backup and logs, keep Worker/Web stopped, and escalate. Never run a down migration or automatic database restore.
- Extraction/current failure: partial extraction stays outside `current`; inspect retained runtime evidence.
- Web/port/health/Worker failure: newly started processes are stopped, evidence is retained, and Worker is never started after an earlier Web failure.
- Application-file rollback may select the retained previous release only after an operator proves it is compatible with the current forward schema.
- Production database recovery is an explicit disaster-recovery procedure requiring DBA authorization and the verified Production backup. Never restore Development or Stage into Production and never rerun Production bootstrap.

The JSON report records package and Stage-report hashes, schema before/target/after, backup path/size/hash, applied migrations, process IDs, port/health results, phases, failure, and the fact that automatic database rollback and database copy are unsupported. It contains no secrets.
