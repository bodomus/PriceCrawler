# Initial database environment provisioning

`scripts/initialize-database-environments.ps1` is the supported one-time provisioning workflow for Test, Stage, and Production.

## Environment names

| Environment | Database | Initial source | Normal future changes |
|---|---|---|---|
| Development | `varprice` | existing working database | Development `Ensure` workflow |
| Test | `varprice_test` | `0001_baseline.sql` | disposable baseline recreation |
| Stage | `varprice_stage` | one logical Development snapshot | deployment forward migrations; a future explicit refresh is a separate operation |
| Production | `varprice_prod` | one logical Development snapshot, exactly once | deployment forward migrations only |

> After initial bootstrap, Production must never be replaced from Development.

The script has no generic `-Force` option and no Production replacement switch. It refuses an initialized Production database when a durable independence marker, `schema_version`, application tables, or user tables are present.

## Prerequisites

1. Stop Development Web, Worker, DataGrip sessions, and other database clients. The script refuses to dump Development while it has open sessions.
2. Ensure Development passes schema version and critical-object validation.
3. Use an authorized provisioning identity. It may be a deploy/admin identity, but it is not the Web/Worker runtime identity.
4. Configure authentication through `.pgpass`, `PGPASSWORD` supplied by a secret store, or container/deployment secrets. The script has no password parameter and never writes full connection strings.
5. Ensure enough free space exists for the Development dump, Stage pre-replacement backup, and Production initial backup.

The script supports native PostgreSQL CLI tools on PATH or the same PostgreSQL 16 tools inside an explicitly named Docker container. It never copies physical data-directory files.

## Dry run

```powershell
.\scripts\initialize-database-environments.ps1 `
    -ToolMode Docker `
    -DockerContainer var_postgres `
    -HostName localhost `
    -Port 55432 `
    -AdminUser var `
    -DevelopmentDatabase varprice `
    -TestDatabase varprice_test `
    -StageDatabase varprice_stage `
    -ProductionDatabase varprice_prod `
    -InitializeAll `
    -ReplaceExistingTest `
    -ReplaceExistingStage `
    -ConfirmInitialProductionBootstrap `
    -WhatIf
```

`-WhatIf` performs read-only connectivity, source-schema, destination-existence, and Production-refusal checks. It prints selected operations, target names, replacement/confirmation state, artifact paths, and validation steps. It does not create databases, dumps, backups, logs, markers, or reports.

## Test initialization

```powershell
.\scripts\initialize-database-environments.ps1 `
    -ToolMode Docker `
    -DockerContainer var_postgres `
    -HostName localhost `
    -Port 55432 `
    -AdminUser var `
    -InitializeTest `
    -ReplaceExistingTest
```

Test is dropped only with `-ReplaceExistingTest`, recreated from `db/migrations/0001_baseline.sql`, and verified at schema version 1. Development business data is not copied; all critical business tables are empty after initialization.

## Stage initial bootstrap

```powershell
.\scripts\initialize-database-environments.ps1 `
    -ToolMode Docker `
    -DockerContainer var_postgres `
    -HostName localhost `
    -Port 55432 `
    -AdminUser var `
    -InitializeStage `
    -ReplaceExistingStage
```

If Stage exists, the script first creates and verifies a custom-format logical backup. It then recreates Stage, restores the verified Development dump, validates schema version/critical objects, and compares critical row counts captured while Development is quiescent.

Normal Stage deployments do not rerun this initial copy. They use forward migrations. A future Development-to-Stage refresh must remain an explicit separately reviewed operation.

## Production one-time bootstrap

```powershell
.\scripts\initialize-database-environments.ps1 `
    -ToolMode Docker `
    -DockerContainer var_postgres `
    -HostName localhost `
    -Port 55432 `
    -AdminUser var `
    -InitializeProduction `
    -ConfirmInitialProductionBootstrap
```

Production initialization requires the confirmation switch even when the target is absent. After restore and verification the script writes a durable database-level independence marker, creates and verifies the initial Production backup, records checksums, and permanently refuses another Development-to-Production bootstrap.

The database-level marker avoids changing the released version-1 application schema. Deleting the marker alone cannot enable overwrite because the script also refuses application/user tables.

## Native PostgreSQL tools

Omit `-DockerContainer` and use `-ToolMode Native` when all required tools are installed:

```powershell
.\scripts\initialize-database-environments.ps1 `
    -ToolMode Native `
    -HostName <postgres-host> `
    -Port 5432 `
    -AdminUser <deployment-user> `
    -InitializeTest `
    -ReplaceExistingTest
```

## Artifacts and verification

Generated operational artifacts are ignored by Git:

```text
artifacts/db/bootstrap/
artifacts/db/backups/stage/
artifacts/db/backups/production/
artifacts/db/logs/
```

Every retained dump is checked for non-zero size, validated with `pg_restore --list`, and assigned a SHA-256 checksum. The latest execution report is written to `database-environments-bootstrap-report.md`.

Schema verification:

```sql
select version, migration_name, applied_at_utc, application_version
from schema_version
order by version;
```

Production independence marker:

```sql
select shobj_description(oid, 'pg_database')
from pg_database
where datname = 'varprice_prod';
```

Restore a retained logical dump only into an approved empty non-Production target unless an independently reviewed recovery procedure explicitly authorizes Production recovery:

```powershell
pg_restore `
    --exit-on-error `
    --no-owner `
    --no-privileges `
    --dbname <empty-target> `
    <verified-dump>
```

Never restore a Development dump over initialized Production.

## Runtime roles and configuration

The provisioning identity is not a runtime identity. Stage and Production Web/Worker must use externally created non-superuser logins with no schema `CREATE` permission. Optional `-StageRuntimeRole`, `-ProductionRuntimeRole`, and `-TestRuntimeRole` parameters apply data/routine grants only to an existing non-superuser role; they never create a role or accept its password.

`config/database-environments.example.json` and environment-specific appsettings files contain placeholders only. Deployment or a secret store must replace the whole connection string:

```text
Development -> varprice
Test        -> varprice_test
Stage       -> varprice_stage
Production  -> varprice_prod
```

Stage and Production remain `DatabaseSchema:StartupMode=ValidateOnly`. The application validates before listening or processing, while deployment owns all schema changes.

