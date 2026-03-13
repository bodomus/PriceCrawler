# Statuses in VarPrice

This file lists the status values that are actively used in the application after the
`crawler_run` / `price_snapshot` refactor.

## 1. Queue item statuses (`price_collect_queue`)

Source: `VarPrice.Domain.Constants.QueueItemStatuses`

### `pending`
- Applied right after a URL is inserted into the queue.
- The item has not been picked up by any worker yet.
- Set in `PgPriceCollectQueueRepository.EnqueueAsync`.

### `reserved`
- Applied when a worker reserves a queue item for processing.
- At the same time `reserved_at`, `lease_until`, and `reserved_by` are filled.
- Set in `PgPriceCollectQueueRepository.ReserveBatchAsync`.

### `succeeded`
- Applied after the product card is processed successfully.
- This is the final successful state for a queue item.
- Set in `PgPriceCollectQueueRepository.MarkSucceededAsync`.

### `retry`
- Applied when processing ends with a transient failure and the item should be retried later.
- At the same time `attempt` is incremented, `last_error_code`, `last_http_status`, and `last_error_message` are saved, and `next_attempt_at` is scheduled.
- Set in `PgPriceCollectQueueRepository.MarkRetryAsync`.
- The same status is also restored by `ReapExpiredReservationsAsync` when an item is stuck in `reserved` and its lease expires.

### `dead`
- Applied when the item should not be processed anymore.
- This happens on a non-transient failure or when `max_attempts` is exhausted.
- This is the final failed state for a queue item.
- Set in `PgPriceCollectQueueRepository.MarkDeadAsync`.

### Queue transitions
- `pending -> reserved`
- `reserved -> succeeded`
- `reserved -> retry`
- `reserved -> dead`
- `reserved -> retry` when the lease expires
- `retry -> reserved`

## 2. Domain run statuses

Source: `VarPrice.Domain.Enums.RunStatus`

### `Running`
- Applied when `crawler_run` and `ingestion_run` are started.
- Means the run exists but has not finished yet.

### `Ok`
- Applied when the queue is fully drained and there are no dead items.
- This is the successful final status of `crawler_run`.

### `Error`
- Applied when the run ends with dead queue items or with a whole-process failure.
- This is the failed final status of `crawler_run`.

## 3. How run statuses are stored in the database

### `crawler_run.status`

`crawler_run.status` is stored as `smallint`:

- `0` -> `RunStatus.Running`
- `1` -> `RunStatus.Ok`
- `2` -> `RunStatus.Error`

There is no separate table of crawler run statuses.

### `ingestion_run.status`

`ingestion_run.status` is still stored as text:

- `running`
- `ok`
- `error`

## 4. Status values returned from the use case

Source: `VarPrice.Application.UseCases.RunCrawlerUseCase`

### `ok`
- Returned in `CrawlerRunResult.Status` when the run finishes successfully.

### `error`
- Returned in `CrawlerRunResult.Status` when the run finishes with an error status.

Important:
- Inside the domain this is `RunStatus.Ok` / `RunStatus.Error`.
- In `crawler_run` it is stored as `smallint`.
- In the result returned to UI and worker it is exposed as `ok` / `error`.

## 5. Snapshot and error semantics

### `price_snapshot`
- Append-only history of meaningful product state changes.
- No status column is used here.
- No new snapshot is created for "same state seen again".

### `product.last_seen_at`
- Updated when a product is seen successfully without a meaningful state change.

### `product_errors`
- Stores structured processing errors with `run_id` and optional links to `product_key`,
  `price_snapshot_id`, and `queue_id`.
- For non-critical parsing issues with a valid product state, the error can point to a created snapshot.
- For critical failures without a valid snapshot, only the error row is saved.

## 6. UI status bar levels

Source: `VarPrice.Web.Controllers.RunsController`, `VarPrice.Web.ViewModels.Shared.StatusBarViewModel`

### `info`
- Shown in the UI after a successful ingestion run.
- Used for a positive completion message.

### `error`
- Shown in the UI when the run result is unsuccessful.
- Used to display an error message to the user.
