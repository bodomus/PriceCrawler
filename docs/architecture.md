# Architecture

## Layer responsibilities

### VarPrice.Domain
- Core entities: `CrawlerRun`, `IngestionRun`, `Product`, `PriceSnapshot`.
- Domain enums/value objects: `RunStatus`, `ErrorInfo`.
- Repository ports: `ICrawlerRunRepository`, `IIngestionRunRepository`, `IPriceSnapshotRepository`.

### VarPrice.Application
- `RunCrawlerUseCase` orchestrates:
  1. Start `crawler_run`.
  2. Start linked `ingestion_run`.
  3. Collect and filter product urls.
  4. Enqueue urls into `price_collect_queue`.
  5. Reserve queue batches with lease (`FOR UPDATE SKIP LOCKED`).
  6. Extract cards and write idempotent snapshot/error records.
  7. Retry transient failures with backoff; move exhausted items to dead-letter state.
  8. Run reaper for expired leases.
  9. Finalize statuses when queue is drained.
- On failure: ingestion receives `ErrorInfo`; crawler run is marked `Error`.

### VarPrice.Infrastructure
- `PgCrawlerRunRepository`, `PgIngestionRunRepository`, `PgPriceSnapshotRepository`.
- `PgPriceCollectQueueRepository` for queue enqueue/reserve/retry/dead/reap/stats.
- `SchemaBootstrapper` ensures required tables/indexes.
- HTTP adapters: `SitemapReader`, `VarusProductCardExtractor`.
- Composition root extension: `AddVarPriceInfrastructure(configuration)`.

### VarPrice.Web
- Razor Pages only call use-cases (`RunCrawlerUseCase`).
- No direct DB access from UI layer.

### VarPrice.Worker
- Standalone console runner.
- Parses CLI args and invokes `RunCrawlerUseCase`.
- No web host required.

## Composition

Both executable apps use:

- `AddVarPriceApplication(configuration)`
- `AddVarPriceInfrastructure(configuration)`

This keeps workflow/business logic reusable for future UI replacements (desktop/other hosts).
