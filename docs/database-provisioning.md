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

Retain the complete dry-run output for audit when required:

```powershell
.\scripts\initialize-database-environments.ps1 <parameters> -WhatIf *>&1 |
    Tee-Object ".\artifacts\db\logs\initialize-environments-whatif.txt"
```

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

## Partial failure recovery

The script never deletes Stage or Production automatically from error handling. When replacement or restore has started but has not completed, it emits `RECOVERY REQUIRED`, the retained verified Development dump, and an exact command adapted to the selected Docker/native tool mode and database names.

For a failed Stage replacement:

1. Keep the verified pre-replacement Stage backup and the Development dump named in the failure log.
2. Confirm that the failed run, rather than a later deployment, owns the incomplete Stage database.
3. Rerun the guarded replacement with the same dump. The existing incomplete Stage is backed up again before it is replaced:

```powershell
.\scripts\initialize-database-environments.ps1 <connection-and-database-parameters> `
    -InitializeStage `
    -ReplaceExistingStage `
    -VerifiedDevelopmentDumpPath "<verified-development-dump>"
```

For a failed initial Production bootstrap before the independence marker was written:

1. Prove from deployment records, application configuration, and access logs that this Production database has never been successfully introduced into service.
2. Confirm that the independence marker is absent:

   ```powershell
   docker exec "<postgres-container>" psql --username "<admin-user>" --dbname postgres --command "select coalesce(shobj_description(oid,'pg_database'),'') from pg_database where datname='<production-database>';"
   ```

3. Only when the failure output says that this script run created the database, terminate its sessions and manually delete that failed bootstrap database. Never use these commands for an established Production database:

   ```powershell
   docker exec "<postgres-container>" psql --username "<admin-user>" --dbname postgres --command "select pg_terminate_backend(pid) from pg_stat_activity where datname='<production-database>' and pid<>pg_backend_pid();"
   docker exec "<postgres-container>" dropdb --username "<admin-user>" "<production-database>"
   ```

4. Repeat the guarded bootstrap from the exact verified dump retained by the failed run:

   ```powershell
   .\scripts\initialize-database-environments.ps1 <connection-and-database-parameters> `
       -InitializeProduction `
       -ConfirmInitialProductionBootstrap `
       -VerifiedDevelopmentDumpPath "<verified-development-dump>"
   ```

If the script did not create the Production database in the failed run, do not delete it; escalate ownership/history verification to the DBA. If the marker exists, Production is already independent: do not delete it and do not rerun bootstrap. Complete only the failed post-marker grant, backup, or verification step through a separately reviewed recovery procedure.

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

The provisioning/deploy identity is not a runtime identity. Provision the four separate logins after the databases exist, without rerunning database bootstrap:

| Environment | Host | Role |
|---|---|---|
| Stage | Web | `pricecrawler_stage_web` |
| Stage | Worker | `pricecrawler_stage_worker` |
| Production | Web | `pricecrawler_prod_web` |
| Production | Worker | `pricecrawler_prod_worker` |

Passwords are accepted only from environment variables populated by the deployment secret store. The script has no password parameters and sends role DDL to `psql` over stdin so credentials are absent from process arguments and logs:

```powershell
$env:PRICECRAWLER_STAGE_WEB_DB_PASSWORD = <read-from-secret-store>
$env:PRICECRAWLER_STAGE_WORKER_DB_PASSWORD = <read-from-secret-store>
$env:PRICECRAWLER_PROD_WEB_DB_PASSWORD = <read-from-secret-store>
$env:PRICECRAWLER_PROD_WORKER_DB_PASSWORD = <read-from-secret-store>

.\scripts\provision-database-runtime-roles.ps1 `
    -ToolMode Docker `
    -DockerContainer var_postgres `
    -AdminUser var `
    -ObjectOwnerRole var `
    -StageDatabase varprice_stage `
    -ProductionDatabase varprice_prod

Remove-Item Env:PRICECRAWLER_STAGE_WEB_DB_PASSWORD,
    Env:PRICECRAWLER_STAGE_WORKER_DB_PASSWORD,
    Env:PRICECRAWLER_PROD_WEB_DB_PASSWORD,
    Env:PRICECRAWLER_PROD_WORKER_DB_PASSWORD
```

Use `-WhatIf` first; it validates connectivity, database existence, and schema version without reading secret values or changing roles/grants. This role script never runs a migration, baseline, bootstrap, restore, or schema-version update.

The roles are `LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS`. They receive `CONNECT`, schema `USAGE`, application table DML, sequence usage, and only the required routine execution rights. Both hosts currently need application DML because the Web `IngestVegetables` action runs the crawler pipeline; Web routine execution is allowlisted to that crawler/read path, while Worker receives the complete operational routine catalog. Neither role owns the database, schema, tables, sequences, or routines.

The script revokes database/schema creation, PUBLIC object access, and any inherited role memberships. It grants only explicit Web/Worker allow-lists for the current schema and actively proves that each runtime role:

- reads schema version `1` through the same read-only contract used by `ValidateOnly`;
- cannot execute `CREATE TABLE`;
- cannot execute `ALTER TABLE`.

Runtime roles receive no automatic default privileges on future objects. Rerun the role script after an approved forward migration so reviewed allow-lists cover newly required tables/sequences/routines. If a new host code path needs another object, add it explicitly to that host's allow-list; do not grant all objects implicitly.

`config/database-environments.example.json` and environment-specific appsettings files contain placeholders only. Deployment or a secret store must replace the whole connection string:

```text
Development -> varprice
Test        -> varprice_test
Stage Web   -> varprice_stage / pricecrawler_stage_web
Stage Worker-> varprice_stage / pricecrawler_stage_worker
Prod Web    -> varprice_prod / pricecrawler_prod_web
Prod Worker -> varprice_prod / pricecrawler_prod_worker
```

Inject the final connection string separately into each process through `ConnectionStrings__Postgres`; do not store it in appsettings or command history.

Stage and Production remain `DatabaseSchema:StartupMode=ValidateOnly`. The application validates before listening or processing, while deployment owns all schema changes.
