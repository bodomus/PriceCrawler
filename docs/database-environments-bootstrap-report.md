# Database environments bootstrap report

Generated UTC: 2026-07-17T05:28:08.9882040Z

- Source: Development database `varprice`
- Expected schema version: 1
- Application version: `v0.4.1-alpha`
- PostgreSQL endpoint: `localhost:55432`
- Tool mode: `Docker`

## Environment results

| Environment | Database | Schema | Data policy |
|---|---|---:|---|
| Test | `varprice_test` | 1 | baseline structure only; no Development business data |
| Stage | `varprice_stage` | 1 | initial consistent Development logical snapshot |
| Production | `varprice_prod` | 1 | one-time Development snapshot; now independent |

## Critical row counts

| Table | Development | Test | Stage | Production |
|---|---:|---:|---:|---:|
| `product` | 4990 | 0 | 4990 | 4990 |
| `price_snapshot` | 8792 | 0 | 8792 | 8792 |
| `crawler_run` | 49 | 0 | 49 | 49 |
| `crawler_run_stage` | 0 | 0 | 0 | 0 |
| `ingestion_run` | 48 | 0 | 48 | 48 |
| `price_collect_queue` | 57902 | 0 | 57902 | 57902 |
| `product_catalog` | 1 | 0 | 1 | 1 |
| `product_catalog_refresh` | 0 | 0 | 0 | 0 |
| `crawl_error` | 12037 | 0 | 12037 | 12037 |

## Logical dump and backup artifacts

| Kind | Path | Bytes | SHA-256 |
|---|---|---:|---|
| Development bootstrap | `J:\Projects\c#\(!!!VARUS)\artifacts\db\bootstrap\varprice-dev-v1-20260717-052756.dump` | 4921274 | `c6c5ebff9be8311a6d88eeeb83da536f9059d22f96ee7b07c8c16fd62880c7f2` |
| Stage pre-bootstrap backup | `J:\Projects\c#\(!!!VARUS)\artifacts\db\backups\stage\varprice-stage-before-bootstrap-20260717-052756.dump` | 893 | `2fb9069c4c9c3d4247cd135ab832343fc716227a73ea60a5df55aa198903cc2c` |
| Production initial backup | `J:\Projects\c#\(!!!VARUS)\artifacts\db\backups\production\varprice-prod-initial-v1-20260717-052756.dump` | 4924646 | `0c43f535546e24113181c371285ec8c835ece2690557a4130dbc41e6bc02e7fa` |

## Production independence

Production was initialized exactly once from the verified Development logical dump. A durable database-level marker records completion, and the provisioning script refuses any future Development-to-Production overwrite.

> After initial bootstrap, Production must never be replaced from Development.

Future Production schema changes use forward migrations only.

## Partial failure recovery policy

The provisioning script does not automatically delete Stage or Production in error handling. A failed Stage replacement is rerun explicitly from the retained verified Development dump. A failed initial Production database may be deleted manually only after proving that it was created by the failed run, was never introduced into service, and has no independence marker. If the marker exists, Production is independent and must not be deleted or bootstrapped again. Exact environment-specific marker, deletion, and guarded rerun commands are emitted as `RECOVERY REQUIRED` by the failing run and documented in `docs/database-provisioning.md`.

## Remaining manual steps

- Populate the four runtime password environment variables from the deployment secret store and run `scripts/provision-database-runtime-roles.ps1`; do not rerun Production bootstrap.
- Inject distinct Web/Worker connection strings for `pricecrawler_stage_web`, `pricecrawler_stage_worker`, `pricecrawler_prod_web`, and `pricecrawler_prod_worker`.
- Supply connection strings through external configuration; do not store credentials in the repository.
- Apply future Stage/Production schema changes through deployment forward migrations before Web or Worker starts.

## Restore command pattern

Use `pg_restore --exit-on-error --no-owner --no-privileges --dbname <empty-target> <verified-dump>` with an authorized deployment identity. Never restore a Development dump over initialized Production.
