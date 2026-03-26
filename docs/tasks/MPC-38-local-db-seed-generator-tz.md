# MPC-38 - Local DB seed generator (TZ)

Issue: https://bodomus.youtrack.cloud/issue/MPC-38  
Project: `MPC`  
Created: 2026-03-26

## Goal

Create one PostgreSQL SQL script for local debugging that:

- fully resets current business data in the local database
- generates a fresh realistic dataset for approximately the last 30 days
- populates the current VarPrice schema with enough volume and variation for UI, analytics, queue, and write-side debugging

## Why

The current project needs a repeatable way to quickly fill a local PostgreSQL database with believable data.
This is required for:

- `/Runs` TreeList and snapshot grid debugging
- price history and analytics panel validation
- queue lifecycle diagnostics
- crawl error scenarios
- DB routines verification on realistic volume

## Scope

The generator must populate the current schema entities:

- `crawler_run`
- `ingestion_run`
- `product`
- `price_collect_queue`
- `price_snapshot`
- `crawl_error`

The solution must be compatible with the current schema and current business semantics from:

- `schema.sql`
- `db/routines/*.sql`
- current `/Runs` queries and UI expectations

## Format

Required delivery format:

- one PostgreSQL `.sql` file

Operational mode:

- manual запуск from `DataGrip` or `psql`
- no dependency on application startup
- no dependency on Web/Worker runtime

Execution mode:

- `reset + generate`

Environment target:

- local/dev only

## Reset Requirements

The script must remove existing business data in a safe dependency order:

- `crawl_error`
- `price_snapshot`
- `price_collect_queue`
- `ingestion_run`
- `crawler_run`
- `product`

If needed, it must also reset identity/sequence values to provide a clean local starting point.

The script is allowed to be destructive because this task is explicitly for local debugging only.

## Generation Window

The default generated period must be approximately:

- last `30` days from execution time

The script should preferably expose editable constants near the top of the file for:

- number of days
- runs per day
- catalog size
- min/max snapshots per run
- error rate
- retry/dead rate
- promo rate
- out-of-stock rate

## Volume Baseline

Default target volume:

- around `30` runs per day
- around `50-150` snapshot attempts per run
- around `5-15%` failed scenarios
- around `2-8%` crawl errors

These values do not need to be exact on every run, but the generator should stay near this scale.

## Realism Requirements

The dataset must be realistic, not purely random noise.

### Catalog realism

- the catalog should be stable across many runs
- the same products should reappear across days
- products should look like a grocery/VARUS-like assortment
- product names, urls, slugs, units, and pack sizes should be coherent

### Price realism

- prices should mostly move gradually
- not every run should create a price change for every product
- `old_price` should appear mainly when promo is active
- promo periods should last for several observations, not flicker every single run
- some products should remain stable for long periods

### Availability realism

- some products should periodically become out of stock
- out-of-stock periods should not affect all products at once

### Queue realism

- most queue items should finish as `succeeded`
- some should pass through `retry`
- some should end as `dead`
- retry and dead cases should carry realistic error fields

### Run realism

- most `crawler_run` / `ingestion_run` records should finish with `ok`
- a smaller portion should finish with `error`
- run start/finish times should look believable and distributed over the day

### Error realism

`crawl_error` should contain plausible combinations of:

- `error_code`
- `http_status`
- `error_message`

Examples of realistic categories:

- timeout
- parse_error
- product_not_found
- throttled
- upstream_5xx

## Architecture Constraints

The script must respect current meanings of statuses.

### crawler_run.status

Allowed values:

- `running`
- `ok`
- `error`

### price_collect_queue.status

Allowed values:

- `pending`
- `reserved`
- `retry`
- `succeeded`
- `dead`

### price_snapshot semantics

- each snapshot must be linked to a valid `run_id`
- each snapshot must be linked to a valid `product_id`
- `queue_id` may be nullable, but when present it must reference a queue item from the same run

## Routine-aware Rule

Implementation must explicitly decide where to use:

- direct `insert/update`
- existing DB routines

Preferred direction:

- use DB routines where they preserve existing business semantics with reasonable complexity
- especially consider routine-based observation/snapshot creation

If some parts are generated with direct SQL for performance or simplicity, that choice must be documented in the script comments.

## Expected Outcome

After running the script on a clean local DB:

- `/Runs` TreeList shows many runs across roughly one month
- successful and failed snapshot groups are represented
- product history contains long enough timelines to make charts meaningful
- queue diagnostics can show retry/dead/outstanding-like states where applicable
- crawl errors exist in realistic proportions
- product cards and history panels have believable data

## Acceptance Criteria

- one SQL script exists and is runnable manually in PostgreSQL
- the script fully resets and regenerates the local debug dataset
- the script produces data for about one month by default
- generated data fills all required business tables
- generated data contains promo, out-of-stock, retry, dead, and crawl-error cases
- generated data works with the current `/Runs` UI and queries without schema mismatch
- the solution is documented in the repository

## Non-goals

- production seeding
- shared staging/public environment seeding
- preserving existing local business data
- exact replay of real crawler traffic

## Notes

This task is intentionally destructive for local data because the team is not yet split across separate `dev/stage/public` branches/environments.
