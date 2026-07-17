# VARUS Price Crawler

Сервис для сбора и обработки данных о товарах VARUS.

## Состав решения

- `PriceCrawler.Domain` - доменные сущности и контракты.
- `PriceCrawler.Application` - use-case и orchestration.
- `PriceCrawler.Infrastructure` - Postgres-репозитории, queue pipeline, bootstrap схемы, HTTP crawler adapters.
- `PriceCrawler.Web` - web/API хост.
- `PriceCrawler.Worker` - консольный запуск crawler.

## Runs Dashboard (MVC + Kendo Analytics)

Экран `Runs` переведен на ASP.NET Core MVC + Kendo UI и теперь использует аналитическую панель товара:

- слева остается навигация `date -> run -> snapshot`;
- справа сохраняется рабочая зона со `Snapshots`;
- нижняя детальная часть больше не показывает старый `Product grid`, а строит:
  - `Product Card`,
  - `Price History`,
  - `Price Chart` с расширенной аналитикой по Postgres history,
  - ручной `Live VARUS` comparison по явному действию пользователя.

На `Этапе 3` экран остается детерминированным по умолчанию:

- `Product Card` показывает выбранный snapshot;
- `Price History` остается paged grid для ручного анализа;
- `Product Card`, `Price History` и `Price Chart` теперь загружаются единым read-only payload `ProductAnalysis` по `snapshotId`.
- Live HTTP-запрос в VARUS выполняется только по явному нажатию `Refresh from VARUS`.
- Результат live-запроса не меняет текущий selection и не пишет новый snapshot в БД автоматически.

### Слои

- `PriceCrawler.Web`
  - `Controllers/RunsController.cs`
  - `ViewModels/Runs/RunsDashboardVm.cs`
  - `ViewModels/Shared/StatusBarViewModel.cs`
  - `Views/Runs/Index.cshtml`
  - `Views/Shared/_Layout.cshtml`
  - `Views/Shared/_StatusBar.cshtml`
  - `wwwroot/js/runs-dashboard.js`
  - `wwwroot/vendor/devextreme/*`
- `PriceCrawler.Application`
  - `Grids/Runs/IRunsGridQuerySource.cs`
  - `Grids/Runs/ISnapshotsGridQuerySource.cs`
  - `Grids/Runs/IProductAnalysisService.cs`
  - `Grids/Runs/IProductDetailsQuerySource.cs`
  - `Grids/Runs/IProductPriceHistoryQuerySource.cs`
  - `Grids/Runs/Dto/*` - DTO для JSON-контракта dashboard API
- `PriceCrawler.Infrastructure`
  - `Queries/Runs/RunsGridQuerySource.cs` - EF query для runs
  - `Queries/Runs/SnapshotsGridQuerySource.cs` - EF query для snapshots
  - `Queries/Runs/ProductAnalysisService.cs` - единый агрегатор product card + history + analytics
  - `Queries/Runs/ProductDetailsQuerySource.cs` - карточка товара по выбранному snapshot
  - `Queries/Runs/ProductPriceHistoryQuerySource.cs` - история цен по `product_id` выбранного snapshot

### MVC маршруты

- `GET /` и `GET /Runs` - экран дашборда.
- `POST /Runs/IngestVegetables` - запуск crawler из dashboard.
- `GET /Runs/RunsGrid` - данные таблицы runs.
- `GET /Runs/SnapshotsGrid` - данные таблицы snapshots.
- `GET /Runs/ProductAnalysis` - единый payload аналитической панели по `snapshotId`:
  `productCard`, `history`, `analytics`.
- `GET /Runs/ProductDetails` - карточка выбранного товара по `snapshotId`.
- `GET /Runs/ProductAnalytics` - полный payload для chart и summary analytics по `snapshotId`.
- `GET /Runs/ProductHistory` - история цены выбранного товара по `snapshotId`.
- `POST /Runs/RefreshLiveProduct` - ручной live-запрос в VARUS по `snapshotId` с comparison against stored snapshot.

Для `POST /Runs/IngestVegetables` используется anti-forgery token.

### Где теперь находится data access для dashboard

- `Web` слой не делает EF/SQL запросы для `Runs`.
- Весь доступ к данным для экрана находится в `PriceCrawler.Infrastructure/Queries/Runs`.
- `/Runs` использует единый application-level контракт `ProductAnalysis` для аналитической панели выбранного товара.
- Фильтрация/сортировка/пагинация для Kendo grid выполняются через `DataSourceRequest`/`ToDataSourceResultAsync`.
- Ручной live refresh использует существующий `IProductCardExtractor`, но не делает write-side действий в БД.

## Требования

- .NET SDK 9.0.311+ (проект таргетится в `net8.0`, но в репо закреплен совместимый установленный SDK)
- PostgreSQL 16+ (или `docker compose`)

## Быстрый запуск

### 1) Поднять инфраструктуру (опционально)

```bash
docker compose up -d postgres
```

### 2) Запустить Web

```bash
dotnet run --project PriceCrawler.Web
```

Health endpoint: `http://localhost:8080/health` (в Docker) или локальный порт Kestrel.

### 3) Запустить Worker

```bash
dotnet run --project PriceCrawler.Worker -- vegetables --once
```

Для ручного обновления постоянного каталога товаров без сбора цен:

```bash
dotnet run --project PriceCrawler.Worker -- catalog-refresh
```

Для ежедневного сбора цен из постоянного каталога без discovery:

```bash
dotnet run --project PriceCrawler.Worker -- collect-prices
```

В интерактивной консоли Worker показывает фиксированную верхнюю панель прогресса
для `vegetables`, `catalog-refresh`, `collect-prices` и `run-all`:
обнаружено, новых, обновлено, выбрано на проверку, ссылок в очереди,
обработано ссылок, успешно, ошибок, текущий этап, текущая ссылка и процент
выполнения. Discovery/catalog счетчики не смешиваются с queue-счетчиками:
`выбрано на проверку` показывает исходный batch, а `ссылок в очереди` растет,
если listing/filter страницы добавляют найденные product URLs в очередь. Нижняя часть консоли продолжает
показывать обычный Serilog-поток. Файловый лог `logs/pricecrawler-worker.log`, его
формат, ротация и уровень логирования не меняются; обновления панели пишутся
только в консоль и не попадают в файл. Если stdout перенаправлен или терминал не
поддерживает динамическую перерисовку, панель автоматически отключается и Worker
использует обычный консольный вывод; причина отключения пишется в диагностику.

## Local debug seed script

Для локальной отладки есть отдельный destructive SQL seed-скрипт:

- `db/seeds/001__local_debug_month.sql`

Что делает script:

- очищает текущие бизнес-данные из локальной БД
- генерирует примерно месяц правдоподобной истории
- заполняет `crawler_run`, `ingestion_run`, `product`, `price_collect_queue`, `price_snapshot`, `crawl_error`
- оставляет один свежий `running` run с `pending` / `reserved` / `retry` queue items для диагностики

Важно:

- запускать только на локальной/dev БД
- script не должен использоваться на shared/stage/public окружениях
- перед запуском схема и DB routines уже должны быть применены

Запуск:

- открой `db/seeds/001__local_debug_month.sql` в `DataGrip` и выполни его против локальной БД `varprice`
- после завершения script сам вернет summary counts по основным таблицам и статусам

## Команды запуска Worker

Основной CLI-контракт Worker теперь задается явной позиционной командой:

- `vegetables [--once]`
- `catalog-refresh`
- `collect-prices`
- `--help` / `-h`

Legacy aliases сохранены для обратной совместимости:

- `--job <name>`
- `--collect-prices`

Ошибочные команды, неизвестные параметры и конфликтующие режимы завершаются до создания host и до DB bootstrap с кодом `2`.
Worker CLI-аргументы не передаются в Generic Host как configuration overrides.

### `--once`

`--once` поддерживается только командой `vegetables`. Для `catalog-refresh` и `collect-prices`
он считается ошибочной опцией и завершает Worker с кодом `2`.

На текущем этапе `--once` сохранен как legacy/no-op для обратной совместимости: `vegetables`
всегда выполняет один run и завершается с кодом:

- `0`, если `result.Status == "ok"` (без учета регистра)
- `1`, если статус не `ok`

Long-running daemon/scheduler mode пока не реализован.

### `vegetables`

Команда:

```bash
dotnet run --project PriceCrawler.Worker -- vegetables
dotnet run --project PriceCrawler.Worker -- vegetables --once
```

Поведение:

- запускает существующий discovery + queue + price snapshot flow для legacy vegetables режима;
- показывает интерактивную progress-панель, если терминал поддерживает динамическую перерисовку;
- legacy запуск без аргументов пока остается alias для `vegetables`.

### `catalog-refresh`

Команда:

```bash
dotnet run --project PriceCrawler.Worker -- catalog-refresh
```

Поведение:

- создает `crawler_run` с source `catalog-refresh` до discovery;
- создает `product_catalog_refresh` session;
- запускает `IProductUrlDiscoveryService`;
- выполняет один batch upsert в `product_catalog` с catalog source `varus` и текущим refresh id;
- после безопасного полного refresh soft-деактивирует активные товары, которые давно не встречались;
- inactive товар автоматически реактивируется при повторном discovery;
- не создает `ingestion_run`, `price_collect_queue`, `price_snapshot` и `product`;
- завершает процесс с кодом `0` при `status=ok` и `1` при ошибке.

Runtime flow:

```text
crawler_run_start
-> refresh_session_start
-> active_count
-> discovery
-> catalog_upsert
-> safety_check
-> deactivate_missing
-> refresh_session_complete
-> crawler_run_finish
```

Soft deactivation lifecycle:

```text
discovered -> active -> missing during refresh -> grace period -> inactive -> discovered again -> reactivated
```

`is_active = false` does not physically delete catalog rows, products, queue history, or price snapshots.

### `collect-prices`

Команда:

```bash
dotnet run --project PriceCrawler.Worker -- collect-prices
```

Поведение:

- создает `crawler_run` с source `price-collection`;
- создает связанный `ingestion_run`;
- выбирает due-товары из `product_catalog` по oldest-first;
- ставит выбранные URL в `price_collect_queue` вместе с `product_catalog_id` и `page_kind`;
- классифицирует URL очереди: product pages идут в `IProductCardExtractor`, Varus listing/filter URLs
  вроде `~brand_` идут в listing extractor;
- listing/filter страницы извлекают product links, нормализуют/дедуплицируют их и добавляют найденные URL обратно
  в очередь как `product_page`;
- пишет `product`, `price_snapshot`, `crawl_error` через существующий observation flow;
- обновляет scheduling state `product_catalog` после финального success/dead;
- не запускает discovery, category seed или catalog upsert.

Oldest-first порядок:

1. `last_checked_at is null`;
2. самый старый `last_checked_at`;
3. стабильный tie-breaker по `id`.

Inactive rows, future `next_check_at` и rows с активным `reserved_until` не выбираются. Истекший catalog lease снова делает row доступной для выбора.

Примеры:

```bash
dotnet run --project PriceCrawler.Worker -- vegetables
dotnet run --project PriceCrawler.Worker -- vegetables --once
dotnet run --project PriceCrawler.Worker -- catalog-refresh
dotnet run --project PriceCrawler.Worker -- collect-prices
dotnet run --project PriceCrawler.Worker -- --help
```

## Коды завершения Worker

- `0` - успешный run (`status=ok`)
- `1` - run завершился с ошибочным статусом
- `2` - передана неподдерживаемая команда, опция или конфликтующие режимы

## Конфигурация

Основные ключи (`appsettings.json`):

- `ConnectionStrings:Postgres`
- `Crawler:SitemapIndexUrl`
- `Crawler:DiscoveryMode` (default `CategorySeeds`; supported values: `CategorySeeds`, `Api`, `Sitemap`)
- `Crawler:CategorySeedUrlsFilePath`
- `Crawler:VegetablesUrlContains`
- `Crawler:MaxProductsPerRun`
- `Crawler:CatalogLeaseSeconds` (default `1800`)
- `Crawler:SuccessfulCheckIntervalHours` (default `24`)
- `Crawler:CatalogFailureBaseDelayMinutes` (default `60`)
- `Crawler:CatalogFailureMaxDelayHours` (default `24`)
- `Crawler:CatalogDeactivationEnabled` (default `true`)
- `Crawler:CatalogMissingGracePeriodDays` (default `14`)
- `Crawler:CatalogMinimumExpectedUrls` (default `1000`)
- `Crawler:CatalogMinimumPreviousRatio` (default `0.5`)
- `Crawler:CatalogRefreshRunningTimeoutMinutes` (default `360`)
- `Crawler:MaxUrls`
- `Crawler:MaxCategoryPagesPerSeed` (default `10`)
- `Crawler:MaxConcurrency` (default `4`)
- `Crawler:RequestsPerSecond` (default `2.0`)
- `Crawler:RequestTimeoutSeconds` (default `15`)
- `Crawler:JitterDelayMsMin` / `Crawler:JitterDelayMsMax` (default `50` / `250`)
- `Crawler:RetryCount` (default `2`)
- `Crawler:RetryBaseDelayMs` (default `500`)
- `Crawler:BreakerFailureThreshold` (default `20`)
- `Crawler:BreakerOpenSeconds` (default `60`)
- `Queue:BatchSize` (default `32`)
- `Queue:PollDelayMs` (default `250`)
- `Queue:LeaseSeconds` (default `90`)
- `Queue:MaxAttempts` (default `3`)
- `Queue:RetryBaseDelayMs` (default `1000`)
- `Queue:RetryMaxDelayMs` (default `30000`)
- `Queue:ReaperIntervalSeconds` (default `15`)

Default Varus category seeds are stored in `PriceCrawler.Worker/config/category-seed-urls.varus.json`.

Limit semantics:

- `Crawler:MaxUrls` limits discovery and catalog refresh size.
- `Crawler:MaxProductsPerRun` limits price collection batch size. In legacy `vegetables` mode it limits discovered URLs
  enqueued into `price_collect_queue`; in `collect-prices` mode it limits due `product_catalog` rows selected.
- `Crawler:VegetablesUrlContains` is still respected by discovery; use an empty value for a full catalog refresh.
- Catalog deactivation safety guards: grace period, absolute minimum accepted URL count, accepted/current-active ratio,
  empty scoped filter, supported full discovery mode (`CategorySeeds`), and a single running refresh per source.
- A stale `product_catalog_refresh` stuck in `running` longer than `CatalogRefreshRunningTimeoutMinutes` is marked
  `error` with `catalog_refresh_abandoned` before a new refresh session starts.
- Refresh session and `crawler_run` final statuses are written by one PostgreSQL routine during catalog-refresh
  completion/failure.
- `Api` and `Sitemap` discovery do not automatically allow deactivation because their full-catalog completeness is not
  confirmed.

Catalog SQL checks:

```sql
select is_active, count(*) from product_catalog group by is_active;

select id, normalized_url, last_discovered_at, last_seen_refresh_id, is_active, deactivated_at, reactivated_at
from product_catalog
order by updated_at desc
limit 100;

select *
from product_catalog_refresh
order by id desc
limit 20;
```

Переопределение через переменные окружения:

- `ConnectionStrings__Postgres`
- `Crawler__SitemapIndexUrl`
- `Crawler__DiscoveryMode`
- `Crawler__CategorySeedUrlsFilePath`
- `Crawler__VegetablesUrlContains`
- `Crawler__MaxProductsPerRun`
- `Crawler__CatalogLeaseSeconds`
- `Crawler__SuccessfulCheckIntervalHours`
- `Crawler__CatalogFailureBaseDelayMinutes`
- `Crawler__CatalogFailureMaxDelayHours`
- `Crawler__CatalogDeactivationEnabled`
- `Crawler__CatalogMissingGracePeriodDays`
- `Crawler__CatalogMinimumExpectedUrls`
- `Crawler__CatalogMinimumPreviousRatio`
- `Crawler__CatalogRefreshRunningTimeoutMinutes`
- `Crawler__MaxUrls`
- `Crawler__MaxCategoryPagesPerSeed`
- `Crawler__MaxConcurrency`
- `Crawler__RequestsPerSecond`
- `Crawler__RequestTimeoutSeconds`
- `Crawler__JitterDelayMsMin`
- `Crawler__JitterDelayMsMax`
- `Crawler__RetryCount`
- `Crawler__RetryBaseDelayMs`
- `Crawler__BreakerFailureThreshold`
- `Crawler__BreakerOpenSeconds`
- `Queue__BatchSize`
- `Queue__PollDelayMs`
- `Queue__LeaseSeconds`
- `Queue__MaxAttempts`
- `Queue__RetryBaseDelayMs`
- `Queue__RetryMaxDelayMs`
- `Queue__ReaperIntervalSeconds`

Коды ошибок crawler, сохраняемые в `crawl_error.error_code`:

- `not_found`
- `too_many_requests`
- `timeout`
- `http_5xx`
- `parse_failed`
- `listing_page_sent_to_product_extractor`
- `listing_no_products_found`
- `listing_parsed`
- `product_links_discovered`
- `unsupported_page_type`
- `ProductUrlDiscoveryUnavailable`
- `unknown`

## Модель хранения результатов обхода

- `crawler_run` хранит именно журнал конкретных запусков crawler, а не справочник crawler-ов.
- `crawler_run.status` хранится как `varchar(32)` со значениями `running`, `ok`, `error`.
- `product` нормализован и использует внутренний PK `product.id`; внешний идентификатор хранится отдельно в `product.external_id`.
- `product_catalog` хранит постоянный каталог обнаруженных товарных URL: `source`, исходный и нормализованный URL,
  metadata discovery, активность, даты проверок, reservation/lease поля и счетчик последовательных ошибок.
  `collect-prices` выбирает активные due rows по oldest-first и обновляет `last_checked_at`, `next_check_at`,
  `consecutive_errors`, `external_id` и `slug` после финального результата обработки.
- `price_collect_queue.product_catalog_id` связывает queue item с исходной записью каталога для scheduling updates.
- `price_collect_queue.page_kind` хранит тип queue item (`product_page`, `listing_page`, `category_page`,
  `sitemap_page`, `api_page`, `unknown`). Listing/filter страницы считаются успешно обработанными после parsing
  ссылок и не записываются как product `parse_failed`.
- `price_snapshot` работает как append-only журнал значимых изменений состояния товара.
- Новый `price_snapshot` создается только если изменилось хотя бы одно из полей:
  `price`, `old_price`, `promo_flag`, `in_stock`.
- Если товар успешно обработан, но его состояние не изменилось, новый snapshot не создается.
  В этом случае обновляется только `product.updated_at`.
- Для нового товара создается запись в `product`, затем первый `price_snapshot`, если удалось собрать
  минимально валидное состояние: известен `url` и есть хотя бы одно из
  `price`, `old_price`, `in_stock`.
- Все внешние связи на товар проходят только через внутренний `product.id`.
- `crawl_error` хранит ошибки с полным контекстом запуска:
  `run_id`, `queue_id`, `product_id`, `url`, `created_at`, `error_code`, `http_status`, `error_message`.
- При некритической ошибке и валидном состоянии товара может быть создан и snapshot, и связанная запись
  в `crawl_error`.
- При критической ошибке без валидного состояния snapshot не создается, сохраняется только `crawl_error`.

## Catalog price collection SQL checks

```sql
select
    id,
    normalized_url,
    last_checked_at,
    next_check_at,
    consecutive_errors,
    reserved_until
from product_catalog
order by last_checked_at nulls first, id
limit 50;
```

```sql
select
    id,
    run_id,
    product_catalog_id,
    status,
    attempt,
    max_attempts
from price_collect_queue
where run_id = :run_id
order by id;
```

## Database schema versioning

- Canonical clean-database entry point: `db/migrations/0001_baseline.sql`.
- Existing version `1` database registration: `db/scripts/bootstrap-schema-version.sql`.
- Current expected schema version is centralized in `DatabaseSchema.ExpectedVersion` and is validated by both Web and Worker.
- `DatabaseSchema:StartupMode=Ensure` initializes an empty Development/Test database from the baseline, or runs the approved existing-database ensure path, and then validates version `1`.
- `DatabaseSchema:StartupMode=ValidateOnly` executes only read-only metadata queries.
- Development and Test configure `Ensure`; Stage, Staging, and Production configure `ValidateOnly`.
- A hard policy permits `Ensure` only in Development/Test. Environment-variable or Web command-line overrides cannot enable it elsewhere; startup aborts before database access.
- Web validates before opening its listening port. Worker validates before resolving or executing crawler work.
- Missing, empty, older, or newer `schema_version` metadata stops startup with an operator-facing error.
- Stage and Production schema changes belong to deployment, not application startup.
- Release ZIPs include `db/migrations`, `db/scripts`, and `database.minimumSchemaVersion` / `targetSchemaVersion` in `release.json`.

Initial Test/Stage/Production provisioning uses `scripts/initialize-database-environments.ps1` and is documented in `docs/database-provisioning.md`. Test is created from the baseline without Development business data; Stage receives a verified logical Development snapshot; Production receives that snapshot exactly once and is then protected by a durable independence marker.

Stage and Production use four separate non-superuser runtime identities provisioned by `scripts/provision-database-runtime-roles.ps1`: distinct Web and Worker roles for each environment. Credentials and complete runtime connection strings come from environment variables or the deployment secret store. Runtime roles run only with `ValidateOnly`, have no database/schema creation or object ownership, and are actively verified to reject `CREATE TABLE` and `ALTER TABLE`.

> After initial bootstrap, Production must never be replaced from Development.

Connection-string placeholders for all four environments are in `config/database-environments.example.json`. Real credentials remain in external configuration or a secret store.
- Detailed operator commands and safety rules: `db/README.md` and `docs/database-environments.md`.
- Schema downgrade is not supported.

## DB routine scripts

- Версионируемые SQL-скрипты DB routines находятся в `db/routines`.
- Формат имени скрипта: `NNN__description.sql`, например `001__routine_support_text.sql`.
- `SchemaBootstrapper` применяет `schema.sql`, затем последовательно выполняет все `db/routines/*.sql`
  в лексикографическом порядке имени файла.
- Для повторяемой поставки используется таблица `db_routine_script`:
  она хранит `script_name`, `script_hash`, `applied_at` и позволяет повторно выполнять только изменившиеся скрипты.
- Скрипты routines должны быть идемпотентными и использовать `create or replace function/procedure`
  или эквивалентный безопасный шаблон.
- Доменные write-side routines именуются по бизнес-операциям, например
  `crawler_run_start`, `crawler_run_finish`, `price_observation_store`,
  `price_collect_queue_reserve_batch`.
- Весь write-side с бизнес-логикой теперь выполняется через DB routines:
  `crawler_run_start`, `crawler_run_finish`,
  `ingestion_run_start`, `ingestion_run_finish`,
  `price_observation_store`,
  `crawl_error_add`.
- Для `price_collect_queue` через DB routines выполняются:
  `price_collect_queue_enqueue`, `price_collect_queue_reserve_batch`,
  `price_collect_queue_mark_succeeded`, `price_collect_queue_mark_retry`,
  `price_collect_queue_mark_dead`, `price_collect_queue_reap_expired`,
  `price_collect_queue_has_outstanding`, `price_collect_queue_get_run_stats`.
- Для `product_catalog` через DB routines выполняются:
  `product_catalog_upsert_discovered`, `product_catalog_get_by_id`,
  `product_catalog_get_by_source_normalized_url`.
- `price_observation_store` инкапсулирует единое доменное действие записи observation:
  поиск existing product, upsert `product`, чтение latest snapshot,
  проверку meaningful change, conditional insert `price_snapshot`
  и возврат `(productId, snapshotId, snapshotCreated)`.
- Общие SQL helper-объекты для будущих routines допускают префикс `routine_support_*`.
- `schema.sql` и `db/routines/**/*.sql` остаются legacy Development initialization assets.
  Canonical deployment creation/registration assets находятся в `db/migrations` и `db/scripts` release-пакета.

## Integration tests for DB routines

- Ключевые write-side сценарии покрыты в `PriceCrawler.Web.Tests/WorkerIntegrationTests.cs`.
- Тесты проверяют:
  `crawler_run`, `ingestion_run`, `price_observation_store`,
  `crawl_error_add`, queue lifecycle, reaper, stats и полную use-case интеграцию.

## Run statistics

Каждый `catalog-refresh` и `price-collection` сохраняет итоговые агрегаты в `crawler_run`; длительности этапов
сохраняются в `crawler_run_stage`. Статистика завершается одним вызовом `crawler_run_complete`, stages передаются
одним JSON batch без update на каждый товар.

- Catalog: `discovered`, `accepted`, `inserted`, `updated`, `reactivated`, `deactivated`.
- Price collection: `selected`, `enqueued`, `succeeded`, `retry`, `dead`, `failed`, `products_created`,
  `products_updated`, `snapshots_created`, `errors_created`.
- `products_created` и `products_updated` берутся из явных флагов `price_observation_store`. Existing product считается
  updated только при изменении его бизнес-полей; повторное observation/no-op не увеличивает `products_updated`.
- Общие поля: `run_type`, `discovery_source`, `duration_ms`, `error_code`, `error_message`.
- Семантика `run-finalization`: application finalization (`refresh`/`ingestion` completion) плюс overhead DB routine
  `crawler_run_complete`, которая сохраняет итоговые counters и batch stages.
- Worker печатает эти же значения из result use case. `run-all` последовательно выводит два независимых summary.
- Каждый invocation Worker получает `ExecutionId`; он добавляется в structured log context и CLI `run-all`, связывая
  catalog и price runs одной команды без изменения DB schema.

Read-only API:

```text
GET /api/crawler-runs?limit=50&runType=price-collection&status=ok
GET /api/crawler-runs/{id}
GET /api/crawler-runs/statistics?from=2026-01-01T00:00:00Z&to=2026-02-01T00:00:00Z&runType=price-collection
```

`limit` — от 1 до 200. Для aggregate без дат используется диапазон 30 дней; максимальный диапазон — 365 дней.
Допустимые фильтры: `runType` — `catalog-refresh`, `price-collection`, `legacy`; `status` — `running`, `ok`, `error`.
Неизвестные значения возвращают `400 Bad Request`, а регистр и внешние пробелы нормализуются.

```sql
select id, run_type, status, started_at, finished_at, duration_ms,
       discovered_count, accepted_count, selected_count, succeeded_count,
       dead_count, snapshots_created_count, error_code
from crawler_run order by id desc limit 50;

select run_id, stage, duration_ms, item_count
from crawler_run_stage where run_id = :run_id order by id;

select run_type, count(*) as runs, avg(duration_ms) as avg_duration_ms,
       sum(succeeded_count) as succeeded, sum(dead_count) as dead
from crawler_run where started_at >= now() - interval '30 days'
group by run_type;
```

## Версионирование (Git tags + Nerdbank.GitVersioning)

В solution используется `Nerdbank.GitVersioning` через корневые `Directory.Build.props` и `version.json`.

- Релизный тег: `vMAJOR.MINOR.PATCH` (пример: `v1.2.3`).
- На самом теге сборка получает `Version=1.2.3`.
- На следующих коммитах после тега в `main/master` версия автоинкрементируется и получает prerelease-суффикс
  `-alpha.<height>`.
- `AssemblyInformationalVersion` включает короткий sha в формате `+g<sha>`.

Как выпустить релиз:

```bash
git tag v1.2.3
git push --tags
```

Как проверить вычисленную версию локально:

```bash
dotnet msbuild PriceCrawler.Application/PriceCrawler.Application.csproj -t:GetBuildVersion -getProperty:Version
dotnet msbuild PriceCrawler.Application/PriceCrawler.Application.csproj -t:GetBuildVersion -getProperty:AssemblyVersion
dotnet msbuild PriceCrawler.Application/PriceCrawler.Application.csproj -t:GetBuildVersion -getProperty:FileVersion
dotnet msbuild PriceCrawler.Application/PriceCrawler.Application.csproj -t:GetBuildVersion -getProperty:AssemblyInformationalVersion
```

## Тесты

```bash
dotnet test PriceCrawler.sln
```


## Как делать backup
docker exec var_postgres pg_dump -U var -d varprice -F c -f /backups/varprice.backup

## Если нужен SQL-дамп
docker exec var_postgres pg_dump -U var -d varprice -f /backups/varprice.sql

## Как восстановить
Из backup-формата:
docker exec -i var_postgres pg_restore -U var -d varprice --clean --if-exists /backups/varprice.backup

Из .sql:
docker exec -i var_postgres psql -U var -d varprice -f /backups/varprice.sql
