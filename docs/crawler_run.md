# crawler_run and price snapshots

## What `crawler_run` stores

`crawler_run` is the journal of concrete crawler executions.

- One row = one run.
- It is not a reference table of crawler definitions.
- `status` is stored as text, not as numeric enum values.

Current `crawler_run` columns:

- `id`
- `started_at`
- `finished_at`
- `status`
- `source`
- `note`

Allowed `crawler_run.status` values:

- `running`
- `ok`
- `error`

Useful index:

- `crawler_run(source, started_at desc)`

## What `product` stores

`product` is the normalized product dimension.

- Internal PK: `product.id`
- External VARUS identifier: `product.external_id`
- Stable product URL: `product.url`
- Optional normalized slug and pack metadata: `slug`, `pack_value`, `pack_unit`

All links from facts and errors to a product go only through `product.id`.

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

## What append-only `price_snapshot` stores

`price_snapshot` keeps only meaningful product state changes.

It is append-only:

- existing rows are not overwritten;
- `captured_at` for existing rows does not change;
- a new snapshot is created only when the observed state actually changed.

Current `price_snapshot` columns:

- `id`
- `run_id`
- `product_id`
- `captured_at`
- `price`
- `old_price`
- `promo_flag`
- `in_stock`
- `queue_id`

Indexes:

- `price_snapshot(product_id, captured_at desc)`
- `price_snapshot(run_id)`

`queue_id` in `price_snapshot` is nullable and is not unique.

## When a snapshot is created

A new row in `price_snapshot` can be created only when the product has a minimally valid observed state:

- `url` is known;
- and at least one of these values is available:
  `price`, `old_price`, `in_stock`.

After that, a new snapshot is inserted only when at least one field changed:

- `price`
- `old_price`
- `promo_flag`
- `in_stock`

If the product is new:

1. a row is inserted into `product`;
2. the first `price_snapshot` is inserted if the observed state is valid;
3. `product.updated_at` is synchronized with the observation timestamp.

## When only `product.updated_at` changes

If a product is processed successfully and its state did not change, a new snapshot is not inserted.

Instead, only:

- `product.updated_at`

is refreshed.

This separates the event "we saw the product again" from the event "the product state changed".

## How `crawl_error` works

`crawl_error` stores processing failures with normalized links to the current schema.

Current columns:

- `id`
- `run_id`
- `queue_id`
- `product_id`
- `url`
- `error_code`
- `http_status`
- `error_message`
- `created_at`

Indexes:

- `crawl_error(run_id)`
- `crawl_error(product_id)`

Write rules:

- If the issue is non-critical and we have a valid product state, the pipeline may store both
  `price_snapshot` and `crawl_error`.
- If the issue is critical and no valid state exists, only `crawl_error` is stored.
- Retry-related transient failures keep their latest context in `price_collect_queue`, while the final
  unrecoverable failure is persisted in `crawl_error`.

## Runs analytics screen

The `Runs` dashboard uses the existing navigation chain:

- date group
- crawler run
- selected `price_snapshot`

In the Stage 3 analytics panel:

- `Product Card` is built from the selected `price_snapshot` joined with `product` and `crawler_run`;
- `Price History` is built from all `price_snapshot` rows with the same `product_id`;
- `Price Chart` is rendered from a dedicated Postgres-only analytics payload built from the same history;
- `Live VARUS` refresh is available only as an explicit user action on the selected product card.

This keeps the screen read-only and deterministic:

- no live VARUS request is made when a snapshot is selected automatically;
- missing optional fields such as image, brand, or category must degrade to placeholders without breaking layout;
- chart analytics can add derived metrics such as deltas, range, and promo / stock coverage without changing snapshot navigation semantics;
- manual live refresh may compare current VARUS values with the selected snapshot, but it must not silently replace the current selection or persist new data without another explicit action.
