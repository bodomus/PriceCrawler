# MPC-81 — investigation

## Workflow

- Level: 2 — database provisioning and environment/deployment change.
- Branch: `Codex/MPC-81`, created from clean `Codex/MPC-80` at `fd4e20de0af0`.
- Graphify: refreshed on the current branch; 2,068 nodes and 5,027 edges.
- CRG: full structural rebuild completed; 279 files, 141,675 nodes and 664,144 edges.
- CRG FTS-only post-processing exceeded the 124-second command timeout. Structural data is current; flows/communities are not used as proof.

## Actual environment inventory (2026-07-17)

The inventory was collected read-only from the running PostgreSQL instance. No database was created, dropped, restored, or modified during investigation.

| Environment | Database | State |
|---|---|---|
| Development | `varprice` | exists; schema version 1; working data |
| Test | `varprice_test` | exists; legacy application tables; `schema_version` missing |
| Stage | `varprice_stage` | exists; empty public schema; `schema_version` missing |
| Production | `varprice_prod` | does not exist; name follows the established `varprice_*` convention |

PostgreSQL runs in Docker, not as a Windows service:

- container: `var_postgres`;
- image: PostgreSQL 16;
- host: `localhost`;
- published port: `55432`;
- container port: `5432`.

PostgreSQL CLI tools are not installed on the Windows PATH. The container contains `psql`, `pg_dump`, `pg_restore`, `createdb`, and `dropdb`, so the provisioning workflow needs an explicit Docker execution mode as well as native-tool support.

## Development source state

`varprice` has schema version 1 (`0001_baseline`, application version `v0.4.1-alpha`). Critical row counts before implementation:

| Table | Rows |
|---|---:|
| `product` | 4,990 |
| `price_snapshot` | 8,792 |
| `crawler_run` | 49 |
| `crawler_run_stage` | 0 |
| `ingestion_run` | 48 |
| `price_collect_queue` | 57,902 |
| `product_catalog` | 1 |
| `product_catalog_refresh` | 0 |
| `crawl_error` | 12,037 |

There were no application sessions connected to Development during inventory. The provisioning script should require Development to remain quiescent while a source dump and comparison counts are captured.

## Existing databases and valuable data

- Production does not exist, so there is no Production data to preserve.
- Stage is empty. Replacement must still require `-ReplaceExistingStage` and create a verified pre-replacement backup.
- Test contains no live rows according to PostgreSQL statistics but has an incomplete legacy schema. Replacement must require `-ReplaceExistingTest`; full Development business data must not be copied.
- `pricecrawler_mpc79_baseline_test` and `pricecrawler_mpc79_source_test` also exist. They are unrelated to the four environment targets and must not be touched.

## Current schema and startup contracts

- `DatabaseSchema.ExpectedVersion` is the single application contract and equals 1.
- `db/migrations/0001_baseline.sql` creates an empty version-1 database and refuses a non-empty public schema.
- `DatabaseSchemaStartupCoordinator` selects `Ensure` or `ValidateOnly`.
- Stage/Staging/Production are guarded to `ValidateOnly`; Development/Test may use `Ensure`.
- Web and Worker both run the shared coordinator before listening or processing.
- No new schema version or baseline change is needed for MPC-81.

The Production independence marker should therefore use durable database-level metadata (`COMMENT ON DATABASE`) rather than adding an application table to an already released version-1 schema. Refusal will also check for `schema_version`, application tables, and non-empty user tables, so deleting only the comment cannot enable overwrite.

## Current configuration and secrets

- Base Web and Worker configuration targets `varprice` on `localhost:55432`.
- Environment files currently select schema startup mode but do not provide safe connection-string templates for Test, Stage, and Production.
- Real credentials already present in legacy local configuration are outside this ticket's cleanup scope and must never be copied into new files or logs.
- The only current login role is the `var` superuser. It may provision databases, but must not be documented as the normal Stage/Production runtime identity.
- Separate non-superuser login/runtime roles require externally supplied credentials and may be created by an operator outside this script. The script can validate/apply grants to explicitly named existing roles without accepting passwords.

## Existing operational scripts

- `scripts/backup_varus.bat` and `scripts/restore_varus.bat` are hard-coded local Development utilities and identify a container by ID. They are not safe environment provisioning paths.
- `scripts/deploy-stage.ps1` deploys binaries/configuration and does not provision databases.
- `scripts/build-release.ps1` packages baseline/bootstrap assets and derives expected schema version from `DatabaseSchema.cs`.
- No current repeatable Test/Stage/Production provisioning script exists.

## Smallest correct change

1. Add `scripts/initialize-database-environments.ps1` with native and explicit Docker tool execution modes.
2. Derive expected schema version/application version from the centralized C# contract.
3. Initialize Test from `0001_baseline.sql` only.
4. Produce one verified custom-format Development dump for Stage/Production operations.
5. Guard Test/Stage replacement; back up Stage before replacement.
6. Refuse Production bootstrap unless it is explicitly confirmed and provably uninitialized.
7. Mark Production independence with durable database metadata and create a verified initial Production backup.
8. Validate schema version, critical objects, row counts, checksums, and `ValidateOnly` startup.
9. Add executable guard/integration tests, safe connection templates, operator docs, bootstrap report, and final review report.

## Safety and recovery

- Only logical PostgreSQL tools may be used; no physical cluster files.
- No generic `-Force` parameter is permitted.
- Test replacement is recoverable by rerunning the baseline workflow.
- Stage replacement creates a checksum-verified backup first.
- Initialized Production is never dropped or restored by the script. Future changes are forward migrations only.
- Dump/backup artifacts and logs stay under ignored `artifacts/db/`.
- Passwords are supplied through `.pgpass`, environment/deployment secret stores, or the container environment; never through script parameters or logs.

