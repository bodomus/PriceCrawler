# MPC-15 - PostgreSQL queue pipeline (historical note)

Issue: https://bodomus.youtrack.cloud/issue/MPC-15  
Project: `MPC`  
Updated after `MPC-21`: 2026-03-24

## Status

This document is kept only as historical context.
The queue-based pipeline from `MPC-15` was implemented and later refined by `MPC-21`.
If current behavior conflicts with the original text of this task, follow the current schema/code/docs.

## What stayed valid from MPC-15

- PostgreSQL remains the durable queue backend.
- Queue reservation is still done in SQL with `FOR UPDATE SKIP LOCKED`.
- Retry, backoff, lease expiration, and reaper logic are still core parts of the pipeline.
- The worker flow is still queue-driven and supports concurrent processing.
- As of `MPC-11`, the queue lifecycle and the rest of the crawler write-side business operations are executed through DB routines.

## What changed in MPC-21

### Normalized product model

The current schema no longer uses legacy `product_key` links.
All product-facing relations go through internal `product.id`.

Current `product` columns:

- `id`
- `external_id`
- `name`
- `url`
- `slug`
- `pack_value`
- `pack_unit`
- `created_at`
- `updated_at`

### Queue table

The current queue table is `price_collect_queue` with this shape:

- `id`
- `run_id`
- `url`
- `status`
- `attempt`
- `max_attempts`
- `next_attempt_at`
- `reserved_at`
- `lease_until`
- `reserved_by`
- `idempotency_key`
- `last_error_code`
- `last_http_status`
- `last_error_message`
- `created_at`
- `updated_at`
- `finished_at`

Important difference:

- `city` was removed from the queue model.

### Snapshot fact table

`price_snapshot` is now the fact table and stores:

- `id`
- `run_id`
- `product_id`
- `captured_at`
- `price`
- `old_price`
- `promo_flag`
- `in_stock`
- `queue_id`

Important differences:

- legacy `snapshot_id` was replaced by `id`
- legacy `regular_price` / `final_price` were replaced by `old_price` / `price`
- `discount_percent` is no longer stored physically
- `queue_id` is nullable and not unique

### Error table

The old `product_errors` concept was replaced by `crawl_error`.

Current `crawl_error` columns:

- `id`
- `run_id`
- `queue_id`
- `product_id`
- `url`
- `error_code`
- `http_status`
- `error_message`
- `created_at`

### Run status storage

`crawler_run.status` is now stored as text:

- `running`
- `ok`
- `error`

not as numeric `smallint` enum values.

## Current source of truth

For the current system behavior, use these documents first:

- `README.md`
- `Status.md`
- `docs/crawler_run.md`
- `docs/architecture.md`
- `schema.sql`
