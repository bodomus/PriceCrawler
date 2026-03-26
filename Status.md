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
-
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

`crawler_run.status` is stored as text:

- `running` -> `RunStatus.Running`
- `ok` -> `RunStatus.Ok`
- `error` -> `RunStatus.Error`

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
- In `crawler_run` it is stored as `running` / `ok` / `error`.
- In the result returned to UI and worker it is exposed as `ok` / `error`.

## 5. Snapshot and error semantics

### `price_snapshot`
- Append-only history of meaningful product state changes.
- No status column is used here.
- No new snapshot is created for "same state seen again".

### `product.updated_at`
- Updated when a product is seen successfully without a meaningful state change.

### `crawl_error`
- Stores structured processing errors with `run_id` and optional links to `product_id` and `queue_id`.
- For non-critical parsing issues with a valid product state, the error points to the normalized `product.id`.
- For critical failures without a valid snapshot, only the error row is saved.

## 6. UI status bar levels

Source: `VarPrice.Web.Controllers.RunsController`, `VarPrice.Web.ViewModels.Shared.StatusBarViewModel`

### `info`
- Shown in the UI after a successful ingestion run.
- Used for a positive completion message.

### `error`
- Shown in the UI when the run result is unsuccessful.
- Used to display an error message to the user.

## 7. Runs dashboard TreeList and snapshot UI statuses

Source: `VarPrice.Web.Controllers.RunsController`, `VarPrice.Web.ViewModels.Runs.RunTreeNodeVm`,
`VarPrice.Web.wwwroot.js.runs-dashboard.js`

### TreeList node types

### `date`
- Root node in the `/Runs` TreeList.
- Groups crawler runs by `StartedAtUtc.Date`.
- Title is formatted as `dd.MM.yyyy (count)`.
- Does not select a concrete run for the snapshots grid.

### `run`
- Child node under a date group.
- Represents one concrete crawler run.
- Selecting it loads all snapshots for that run.

### `successful`
- Child node under a run.
- Represents the subset of snapshots that are considered successful in the UI.
- Selecting it loads only successful snapshots for the selected run.

### `failed`
- Child node under a run.
- Represents the subset of snapshots that are considered failed in the UI.
- Selecting it loads only failed snapshots for the selected run.

### Snapshot scopes used by the `/Runs` page

### `none`
- Used for date nodes.
- Means the snapshots grid should stay empty and must not fail.

### `all`
- Used for run nodes.
- Means the snapshots grid loads all snapshots for the selected run.

### `successful`
- Used for the `Successful snapshots` node.
- Means the snapshots grid loads only snapshots where `IsSuccessful == true`.

### `failed`
- Used for the `Failed snapshots` node.
- Means the snapshots grid loads only snapshots where `IsSuccessful == false`.

### UI snapshot statuses

### `OK`
- Returned by `SnapshotsGrid` for snapshots where no linked `crawl_error` row exists for the same `run_id` and `product_id`.
- This is the UI label used for successful snapshots.

### `Failed`
- Returned by `SnapshotsGrid` for snapshots that have at least one linked `crawl_error` row for the same `run_id` and `product_id`.
- This is the UI label used for failed snapshots.

Important:
- `price_snapshot` still has no physical `status` column in the database.
- `OK` / `Failed` are derived UI statuses.
- The current rule is:
  `OK` = no `crawl_error` linked by `run_id + product_id`
  `Failed` = one or more `crawl_error` linked by `run_id + product_id`

## 8. Manual live refresh result statuses

Source: `VarPrice.Web.Controllers.RunsController`, `VarPrice.Application.Models.ProductExtractResult`,
`VarPrice.Web.wwwroot.js.runs-dashboard.js`

### `success`
- Returned by `RefreshLiveProduct` when VARUS extraction yields a complete product card without an issue.
- Used in the UI when live data is available and the extractor reported no warning condition.

### `partial`
- Returned by `RefreshLiveProduct` when the extractor produced a product card together with a non-critical issue.
- Used to show that live comparison is possible, but the result should be treated as incomplete.

### `error`
- Returned by `RefreshLiveProduct` when no usable live product card could be extracted.
- Used for timeout, HTTP, parse, and other critical extractor failures.

Important:
- These are UI/API result statuses for the explicit manual live check.
- They are not stored in `price_snapshot` or `crawler_run`.
- A manual live refresh does not create a new snapshot automatically.
