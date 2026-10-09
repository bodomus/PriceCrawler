# MPC-79 Database Schema Inventory

## Canonical schema version 1 inventory

### Tables

- `crawler_run`
- `crawler_run_stage`
- `ingestion_run`
- `product`
- `product_catalog`
- `product_catalog_refresh`
- `price_collect_queue`
- `price_snapshot`
- `crawl_error`
- `db_routine_script`
- `schema_version` (new migration metadata)

### Sequences

Identity/serial sequences owned by the corresponding primary-key columns for `crawler_run`, `crawler_run_stage`, `ingestion_run`, `product`, `product_catalog`, `product_catalog_refresh`, `price_collect_queue`, `price_snapshot`, and `crawl_error`.

### Constraints

- Primary keys for every table.
- Foreign keys for ingestion-to-run, queue-to-run/catalog, snapshot-to-run/product/queue, error-to-run/queue/product, catalog-to-refresh, and run-stage-to-run.
- Unique catalog URL, product URL, queue idempotency/run URL, running refresh, and run-stage contracts.
- Existing status, date-ordering, and non-negative-count checks.

### Indexes

All indexes defined by current `schema.sql`, including queue reservation/pick indexes, product/catalog uniqueness, crawler-run analytics indexes, snapshot/error indexes, and the reconciled `ix_product_catalog_due` definition containing `reserved_until`.

### Routines

All current routines from:

- `001__routine_support_text.sql`
- `010__run_error_routines.sql`
- `020__queue_routines.sql`
- `030__observation_routines.sql`
- `040__product_catalog_routines.sql`
- `050__crawler_run_statistics.sql`

The applied script hashes are required reference metadata for a newly baselined database.

### Views, materialized views, triggers, extensions

- Required views: none.
- Required materialized views: none.
- Required triggers: none.
- Required non-default PostgreSQL extensions: none.

## Intentionally excluded Development objects

- `__EFMigrationsHistory` and its index: unused legacy EF metadata.
- Obsolete `product_catalog_upsert_discovered(p_items text)` overload.
- Owners, grants, passwords, connection information, dump restriction tokens, and machine-specific dump metadata.

These additional Development objects are non-blocking because they do not replace or conflict with the required version `1` objects.

