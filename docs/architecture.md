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
  3. Collect product urls and parse cards.
  4. Upsert product and insert snapshot.
  5. Finalize statuses.
- On failure: ingestion receives `ErrorInfo`; crawler run is marked `Error`.

### VarPrice.Infrastructure
- `PgCrawlerRunRepository`, `PgIngestionRunRepository`, `PgPriceSnapshotRepository`.
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
