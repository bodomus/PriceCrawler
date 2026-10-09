# PriceCrawler database deployment

Stage migration orchestration belongs to `Scripts/deploy-stage.ps1` (see `docs/stage-deployment.md`). It requires existing `schema_version`, never runs baseline/bootstrap, verifies a backup, forbids downgrade, and invokes packaged role provisioning with `-StageOnly` after schema work so runtime roles never receive migration/DDL authority and Production is untouched.

PriceCrawler uses explicit forward-only database schema versions. Application version and database schema version are independent.

## Release package contract

`scripts/build-release.ps1` packages deployable database assets under the ZIP root `db/`: all validated numbered migrations, `scripts/bootstrap-schema-version.sql`, runtime-role provisioning support, and this operator reference. `release.json` records the ordered migration filenames and the minimum/target schema range read from `DatabaseSchema.ExpectedVersion`; the current range is `1 -> 1`.

Packaging only copies and validates SQL. It never connects to PostgreSQL, executes baseline/bootstrap/migrations, creates a database, or changes `schema_version`. Dumps, backups, logs, secrets and environment-specific credentials are forbidden. Deployment applies approved forward migrations with the administrative identity, reapplies reviewed runtime grants, and then starts Web/Worker in `ValidateOnly`.

Current baseline:

- application release: `v0.4.1-alpha`;
- schema version: `1`;
- expected version in code: `DatabaseSchema.ExpectedVersion`.

The environment policy in `docs/database-environments.md` remains the source of truth.

## Baseline versus bootstrap

`migrations/0001_baseline.sql` creates a new database from an empty `public` schema. It creates every required table, sequence, constraint, index, routine, routine hash record, and `schema_version` row. It intentionally fails if application objects already exist. Never apply the baseline to an existing Stage or Production database.

`scripts/bootstrap-schema-version.sql` is for an existing schema that already matches version `1`. It validates required objects first and then creates/inserts only `schema_version`. It does not recreate, drop, rename, convert, truncate, or update application objects or data. Additional non-conflicting tables, indexes, columns, constraints, routines, and grants are allowed.

## Create a new empty database

Development or Test example using placeholders:

```powershell
createdb --host <host> --port <port> --username <admin-user> <empty-database>
psql --host <host> --port <port> --username <migration-user> `
    --dbname <empty-database> --set ON_ERROR_STOP=1 `
    --file .\db\migrations\0001_baseline.sql
```

The migration runs in a transaction. PostgreSQL 16 supports all version `1` operations transactionally.

## Bootstrap an existing Development database

Create and verify a backup before reconciling structural differences. Then run:

```powershell
psql --host <host> --port <port> --username <migration-user> `
    --dbname <development-database> --set ON_ERROR_STOP=1 `
    --file .\db\scripts\bootstrap-schema-version.sql
```

Running bootstrap repeatedly is safe when the existing version `1` metadata is identical. Conflicting metadata fails without being overwritten.

Database names containing a `prod` or `production` segment are refused. After the mandatory Production backup and explicit operator approval, the guard can be overridden for that single session:

```sql
SET pricecrawler.allow_production_bootstrap = 'true';
\i db/scripts/bootstrap-schema-version.sql
```

The override does not weaken structural validation.

## Inspect and validate compatibility

```sql
SELECT version, migration_name, applied_at_utc, application_version, checksum
FROM schema_version
ORDER BY version;

SELECT COALESCE(MAX(version), 0) AS current_schema_version
FROM schema_version;
```

Web and Worker both validate the maximum registered version against `DatabaseSchema.ExpectedVersion` at startup. Missing, empty, older, and newer metadata are errors.

## Environment safety

- Development: `DatabaseSchema:StartupMode=Ensure`. An empty database is initialized from `0001_baseline.sql`; an existing approved Development schema uses the legacy ensure path. Version validation always follows.
- Test: `DatabaseSchema:StartupMode=Ensure`. Disposable databases are initialized from the baseline and may be recreated; repeated initialization is deterministic.
- Stage/Staging: `DatabaseSchema:StartupMode=ValidateOnly`. Apply migrations in the deployment process after backup.
- Production: `DatabaseSchema:StartupMode=ValidateOnly`. Apply approved forward migrations only after a verified backup.

`Ensure` is permitted only when the effective environment name is `Development` or `Test`. If Stage, Staging, Production, or an unknown environment is configured as `Ensure`, startup throws `DatabaseSchemaStartupConfigurationException` before the initializer or version reader accesses the database. Environment variables and Web command-line configuration cannot bypass this policy.

Validation is mandatory; there is no `ValidateOnStartup` disable switch. `ValidateOnly` executes only the two read-only metadata queries in `DatabaseSchemaVersionReader`. It never calls the initializer, baseline, bootstrap, migration scripts, or `SchemaBootstrapper`.

Stage and Production schema changes belong to deployment, not application startup.

## Initial environment provisioning

Use `scripts/initialize-database-environments.ps1`; see `docs/database-provisioning.md` for exact Docker and native commands. Test is recreated from `0001_baseline.sql` without Development data. Stage is initialized from a checksum-verified custom-format Development dump and is backed up before replacement. Production can be initialized from Development exactly once, receives a durable database-level independence marker, and gets an initial verified backup.

> After initial bootstrap, Production must never be replaced from Development.

The provisioning script has no Production replacement or generic force option. Future Production changes use approved forward migrations only.

Partial Stage/Production bootstrap failures are never deleted automatically. The script prints marker checks and an exact guarded rerun command using `-VerifiedDevelopmentDumpPath`; follow the audited recovery procedure in `docs/database-provisioning.md` before any manual Production deletion.

Provision Stage/Production runtime identities separately with `scripts/provision-database-runtime-roles.ps1`. It creates distinct Web/Worker logins from externally supplied secret environment variables, grants application data/routine access without ownership or DDL rights, and verifies `ValidateOnly`, `CREATE TABLE` denial, and `ALTER TABLE` denial. It does not run bootstrap or migrations and must be rerun after an approved forward migration to cover new objects.

## Configuration precedence

The effective mode is resolved before the hard safety policy runs:

1. `appsettings.json` provides the safe `ValidateOnly` fallback.
2. `appsettings.<Environment>.json` selects the environment intent.
3. Environment variables such as `DatabaseSchema__StartupMode` override JSON.
4. Web command-line configuration has higher precedence than JSON/environment configuration.
5. Worker operational CLI arguments are deliberately excluded from Generic Host configuration (`Args = []`); environment variables remain supported.
6. Tests may add an explicit in-memory provider last.

The safety policy evaluates the final value, so higher-precedence configuration can cause startup to fail but cannot make a protected environment mutable.

## Startup failure behavior

- Web performs schema startup before `app.Run()` and therefore opens no listening port after failure.
- Worker performs schema startup before command-start logging, use-case resolution, queue consumption, or crawler work.
- Unsafe configuration, missing metadata, empty metadata, older versions, and newer versions all terminate startup with a non-zero process exit.
- Structured logs include `Environment`, `SchemaStartupMode`, `ExpectedSchemaVersion`, `ActualSchemaVersion`, `Result`, and a stable `Reason`; connection strings and credentials are never included.

Runtime users need read access to `schema_version`; they do not need DDL rights. Migration/bootstrap users may require DDL rights.

## No downgrade support

Normal Production schema delivery is owned by `Scripts/deploy-production.ps1`. It requires the exact Stage-approved package, verifies the independent Production marker and separate runtime identities, creates and validates a Production backup, and runs only missing forward migrations with the deploy identity. It never copies Development/Stage into Production, reruns bootstrap, downgrades, or restores the database automatically. See `docs/production-deployment.md`.

Schema versions only move forward (`1 -> 2 -> 3`). There are no down migrations and no automatic schema rollback. Roll back the application only when it remains compatible with the installed database schema; database recovery uses the approved backup process.
