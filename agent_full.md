# VARUS Project Study

## 1. Purpose

`VARUS Price Crawler` is a .NET solution for collecting product data from the VARUS website, normalizing product cards, storing price snapshots in PostgreSQL, and showing crawler runs, snapshots, and product analytics in a web dashboard.

The project already has a separated multi-project structure and is moving toward a clean/layered architecture, but in practice it is currently a hybrid of:

- clean-style layers (`Domain` -> `Application` -> `Infrastructure`)
- dedicated host applications (`Web`, `Worker`)
- CQRS-like separation between DB-routine write side and query-oriented dashboard read side

## 2. Technology Stack

### Platform

- .NET target framework: `net8.0`
- SDK pinned in repo: `.NET SDK 9.0.311` via `global.json`
- C# language version: `12`
- Nullable reference types: enabled
- Implicit usings: enabled
- .NET analyzers: enabled

### Main libraries and frameworks

- ASP.NET Core MVC: web host and dashboard
- Entity Framework Core 8: read/query side for dashboard grids
- Npgsql + PostgreSQL: storage and low-level persistence
- PostgreSQL DB routines + ADO.NET helpers: write-side business operations and queue lifecycle
- Serilog: structured logging for `Web` and `Worker`
- AngleSharp: HTML parsing of product pages
- `System.Threading.RateLimiting`: request throttling
- Kendo UI for ASP.NET Core: dashboard grids and widgets
- xUnit: tests
- DotNet.Testcontainers: integration-test infrastructure dependency

### Infra / Ops

- Docker / docker-compose for PostgreSQL
- Nerdbank.GitVersioning + SourceLink for versioning and build metadata

## 3. Solution Structure

The solution contains 6 projects:

- `VarPrice.Domain`
- `VarPrice.Application`
- `VarPrice.Infrastructure`
- `VarPrice.Web`
- `VarPrice.Worker`
- `VarPrice.Web.Tests`

## 4. Architectural Approach

### Real architectural style

The project is best described as:

- layered architecture
- clean architecture inspired separation of concerns
- hybrid CQRS-like split for reads vs writes
- transitional architecture with some legacy logic still in `Web`

It is **not** a full DDD/Clean Architecture implementation in the strict sense, because:

- the domain layer is small and mostly contains entities/contracts, not rich business aggregates
- application layer contains orchestration/use-case logic but no full mediator/CQRS pipeline
- infrastructure mixes EF Core query models with DB-routine backed repositories
- some historical documents still describe older ingestion paths, but the active dashboard trigger uses the shared application use case

### Layer responsibilities

#### `VarPrice.Domain`

Contains:

- domain entities
- enums and value objects
- repository contracts
- queue-related shared models

This layer defines the business vocabulary and persistence contracts, but keeps logic relatively light.

#### `VarPrice.Application`

Contains:

- use cases
- options/config models
- application abstractions for crawling/parsing
- DTO/contracts for dashboard grids

This is the orchestration layer. The main business process is concentrated in `RunCrawlerUseCase`.

#### `VarPrice.Infrastructure`

Contains:

- PostgreSQL repositories
- schema bootstrapper
- EF Core `DbContext`
- crawler adapters
- query sources and analysis services for dashboard read models

This layer implements all technical concerns: database access, DB routine execution, HTTP crawling, throttling, parsing, and dashboard query building.

#### `VarPrice.Web`

Contains:

- ASP.NET Core MVC host
- dashboard controller/views/view models
- Kendo UI integration
- logging bootstrap
- dashboard endpoints for runs, snapshots, analytics, and manual live refresh

#### `VarPrice.Worker`

Contains:

- console/host entry point
- registration of application + infrastructure services
- command-line execution for a crawler job

This is the main batch/ingestion host.

## 5. Main Execution Flows

### 5.1 Worker flow (primary modern flow)

Main path:

1. `VarPrice.Worker/Program.cs`
2. `RunCrawlerUseCase`
3. `SitemapReader`
4. enqueue URLs into `price_collect_queue`
5. reserve batches from queue
6. process pages in parallel through `VarusProductCardExtractor`
7. persist normalized products, price snapshots, and crawl errors
8. finish `crawler_run` and `ingestion_run`

Important characteristics:

- queue-driven processing
- retry and dead-letter style behavior
- max concurrency support
- request throttling and jitter
- simple circuit-breaker behavior
- idempotency through `queue_id` and `idempotency_key`

### 5.2 Web flow (dashboard / analytics path)

Main UI path:

1. `VarPrice.Web/Program.cs`
2. `RunsController`
3. MVC views + Kendo UI widgets
4. query sources plus a unified product analysis service for run/snapshot/product analytics data

There is also a crawler trigger from the dashboard:

- `POST /Runs/IngestVegetables`
- `RunsController -> IRunCrawlerUseCase`

This path uses the same queue-based orchestration as the worker host and starts the shared crawler application flow from the dashboard.

## 6. Project-by-Project Class Map

### 6.1 `VarPrice.Domain`

#### Entities

- `CrawlerRun`
- `IngestionRun`
- `Product`
- `PriceSnapshot`

#### Supporting types

- `RunStatus`
- `ErrorInfo`
- `QueueEnqueueItem`
- `ReservedQueueItem`
- `QueueRunStats`
- `QueueItemStatuses`

#### Contracts

- `ICrawlerRunRepository`
- `IIngestionRunRepository`
- `IPriceCollectQueueRepository`
- `IPriceSnapshotRepository`
- `IClock`
- `IUnitOfWork`

Observation:

`IClock` and `IUnitOfWork` exist, but they are not central to the current implementation. The project relies more on explicit repository boundaries, DB routines for write-side operations, and query services for dashboard read models.

### 6.2 `VarPrice.Application`

#### Abstractions

- `IProductUrlSource`
- `IProductCardExtractor`

#### Core use case

- `RunCrawlerUseCase`

This class is the heart of the modern ingestion flow. It:

- starts runs
- collects sitemap URLs
- filters URLs
- seeds the queue
- drains queue batches in parallel
- persists snapshots and product errors
- decides retry vs dead based on `QueueRetryPolicy`

#### Configuration models

- `CrawlerOptions`
- `QueueOptions`
- `UrlFilterOptions`

#### Result/data models

- `CrawlerRunResult`
- `IngestionRunResult`
- `ProductCard`
- `ProductExtractResult`
- `CrawlerErrorCodes`

#### Dashboard read-model contracts

- `IRunsGridQuerySource`
- `ISnapshotsGridQuerySource`
- `IProductsGridQuerySource`
- `IProductAnalysisService`
- DTO and QueryRow classes under `Grids/Runs/*`

### 6.3 `VarPrice.Infrastructure`

#### Crawler adapters

- `SitemapReader`
- `VarusProductCardExtractor`
- `VarusRequestCoordinator`

Responsibilities:

- loading XML sitemap index and nested sitemaps
- filtering excluded URLs
- parsing product HTML / JSON-LD
- extracting external id, price, promo state, slug, and pack metadata
- rate limiting and request pacing
- retry backoff and temporary failure coordination

#### Persistence

- `PgConnectionFactory`
- `SchemaBootstrapper`
- `PgCrawlerRunRepository`
- `PgIngestionRunRepository`
- `PgPriceSnapshotRepository`
- `PgPriceCollectQueueRepository`
- `VarPriceDbContext`

Important detail:

the project uses **two persistence styles at once**:

- DB routines plus ADO.NET helpers for write/update business flows
- EF Core / SQL query sources for dashboard read models and ad hoc analysis payloads

#### Query side for dashboard

- `RunsGridQuerySource`
- `SnapshotsGridQuerySource`
- `ProductsGridQuerySource`
- `ProductDetailsQuerySource`
- `ProductPriceHistoryQuerySource`
- `ProductAnalysisService`

This is effectively a read-model layer tailored for the UI, including a unified `ProductAnalysis` payload for the analytics panel.

### 6.4 `VarPrice.Web`

#### Hosting and composition

- `Program.cs`
- `LoggingBootstrapper`

#### MVC dashboard

- `RunsController`
- `RunsDashboardVm`
- `StatusBarViewModel`
- `Views/Runs/Index.cshtml`
- `wwwroot/js/runs-dashboard.js`

#### Dashboard-specific analytics support

- `RunsController`
- `GET /Runs/ProductAnalysis`
- `wwwroot/js/runs-dashboard.js`

Observation:

The dashboard now loads product card, history, and chart analytics through one application-level analysis contract, while manual live refresh remains a separate explicit action.

### 6.5 `VarPrice.Worker`

Contains a thin host:

- builds `HostApplicationBuilder`
- registers `AddVarPriceApplication`
- registers `AddVarPriceInfrastructure`
- ensures schema
- parses CLI args (`--once`, `--job`)
- runs `RunCrawlerUseCase`

This is a good sign: business flow is not embedded in the entry point.

## 7. Data Model and Storage

Main tables in PostgreSQL:

- `crawler_run`
- `ingestion_run`
- `price_collect_queue`
- `product`
- `crawl_error`
- `price_snapshot`

### Meaning of tables

- `crawler_run`: high-level run metadata
- `ingestion_run`: ingestion-specific execution tracking and errors
- `price_collect_queue`: queue of URLs to process with attempts, leases, retry timing
- `product`: deduplicated product catalog
- `crawl_error`: failed product processing attempts linked through normalized `product.id`
- `price_snapshot`: collected price snapshots for a run/product fact

### Important persistence design choices

- uniqueness on `(run_id, url)` in queue
- separate `idempotency_key` for queue insertion safety
- all product-facing links use internal `product.id`
- `queue_id` is nullable and non-unique in `price_snapshot` and `crawl_error`
- retries managed by status fields and `next_attempt_at`
- lease-based reservation for parallel workers

This gives the crawler a simple but effective durable queue model directly in PostgreSQL.

### Write-side routine layer

The current write-side is implemented through versioned routines from `db/routines`:

- `crawler_run_start`
- `crawler_run_finish`
- `ingestion_run_start`
- `ingestion_run_finish`
- `price_observation_store`
- `crawl_error_add`
- `price_collect_queue_enqueue`
- `price_collect_queue_reserve_batch`
- `price_collect_queue_mark_succeeded`
- `price_collect_queue_mark_retry`
- `price_collect_queue_mark_dead`
- `price_collect_queue_reap_expired`
- `price_collect_queue_has_outstanding`
- `price_collect_queue_get_run_stats`

## 8. Configuration

Main configuration sections:

- `ConnectionStrings:Postgres`
- `Crawler`
- `Queue`
- `Serilog`

### Crawler config includes

- sitemap URL
- vegetables URL filter
- max products / max URLs
- concurrency
- requests-per-second
- timeout
- jitter
- retry count and base delay
- circuit-breaker thresholds

### Queue config includes

- batch size
- poll delay
- lease duration
- max attempts
- retry backoff settings
- reaper interval

### URL exclusion filters

Loaded from:

- `VarPrice.Web/config/url-filters.json`
- `VarPrice.Worker/config/url-filters.json`

Current example filters:

- `Test`
- `Sandbox`
- `tmp-`

## 9. Read/Write Split

There is a small but meaningful CQRS-like split:

- write side: raw SQL repositories
- read side: EF Core query sources for dashboard grids

The application is not using formal CQRS tooling, but the design direction is similar:

- command/orchestration side is optimized for ingestion workflow
- query side is optimized for UI data retrieval

## 10. Testing

Test project: `VarPrice.Web.Tests`

Current coverage areas include:

- `RunCrawlerUseCase`
- queue retry policy
- sitemap reader
- parser/extractor behavior
- DB error mapping
- MVC controller behavior
- repository behavior
- integration tests against PostgreSQL

Notable point:

`WorkerIntegrationTests` validate the queue-based flow end-to-end, including:

- run persistence
- queue draining
- reservation isolation across workers
- reaper behavior
- idempotent snapshot/crawl-error persistence

## 11. Architectural Assessment

### Strengths

- good project separation
- thin host applications
- orchestration moved into application layer
- durable queue model in PostgreSQL
- throttling/retry/resiliency built into crawler
- read-side query sources isolated from controller
- test coverage exists for both unit and integration scenarios

### Current limitations / technical debt

- two parallel ingestion implementations exist:
  - modern queue-based flow in `Worker` / `Application`
  - older direct flow in `Web`
- domain layer is relatively anemic
- persistence approach is mixed between EF Core and hand-written SQL
- some UI/web storage classes still duplicate responsibilities already represented elsewhere
- there are signs of transition rather than a fully unified architecture

### Best summary of the architecture today

The project is a **layered .NET 8 crawler system with a clean-ish separation of concerns, a PostgreSQL-backed durable work queue, an MVC dashboard, and a partially retained legacy web ingestion path**.

## 12. Most Important Files to Read First

If a new agent or developer needs fast onboarding, start here:

- `README.md`
- `VarPrice.Worker/Program.cs`
- `VarPrice.Application/UseCases/RunCrawlerUseCase.cs`
- `VarPrice.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- `VarPrice.Infrastructure/Crawler/SitemapReader.cs`
- `VarPrice.Infrastructure/Crawler/VarusProductCardExtractor.cs`
- `VarPrice.Infrastructure/Crawler/VarusRequestCoordinator.cs`
- `VarPrice.Infrastructure/Persistence/PgCrawlerRepositories.cs`
- `VarPrice.Infrastructure/Persistence/SchemaBootstrapper.cs`
- `VarPrice.Infrastructure/Persistence/VarPriceDbContext.cs`
- `VarPrice.Web/Program.cs`
- `VarPrice.Web/Controllers/RunsController.cs`
- `schema.sql`

## 13. Practical Conclusion

For further development, it is safest to think of the repo like this:

- `Worker + Application + Infrastructure` is the main ingestion architecture
- `Web` is the dashboard host and still contains a legacy direct-ingestion path
- PostgreSQL is not only storage, but also the queue engine
- the system favors explicit repositories and procedural orchestration over rich domain modeling

If the project continues to evolve, the most likely architectural cleanup step would be to unify the dashboard-triggered crawl flow with `RunCrawlerUseCase`, so the solution has one ingestion pipeline instead of two.
