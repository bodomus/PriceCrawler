# VARUS Price Crawler

Сервис для сбора и обработки данных о товарах VARUS.

## Состав решения

- `VarPrice.Domain` - доменные сущности и контракты.
- `VarPrice.Application` - use-case и orchestration.
- `VarPrice.Infrastructure` - Postgres-репозитории, queue pipeline, bootstrap схемы, HTTP crawler adapters.
- `VarPrice.Web` - web/API хост.
- `VarPrice.Worker` - консольный запуск crawler.

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

- `VarPrice.Web`
  - `Controllers/RunsController.cs`
  - `ViewModels/Runs/RunsDashboardVm.cs`
  - `ViewModels/Shared/StatusBarViewModel.cs`
  - `Views/Runs/Index.cshtml`
  - `Views/Shared/_Layout.cshtml`
  - `Views/Shared/_StatusBar.cshtml`
  - `wwwroot/js/runs-dashboard.js`
  - `wwwroot/vendor/devextreme/*`
- `VarPrice.Application`
  - `Grids/Runs/IRunsGridQuerySource.cs`
  - `Grids/Runs/ISnapshotsGridQuerySource.cs`
  - `Grids/Runs/IProductAnalysisService.cs`
  - `Grids/Runs/IProductDetailsQuerySource.cs`
  - `Grids/Runs/IProductPriceHistoryQuerySource.cs`
  - `Grids/Runs/Dto/*` - DTO для JSON-контракта dashboard API
- `VarPrice.Infrastructure`
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
- Весь доступ к данным для экрана находится в `VarPrice.Infrastructure/Queries/Runs`.
- `/Runs` использует единый application-level контракт `ProductAnalysis` для аналитической панели выбранного товара.
- Фильтрация/сортировка/пагинация для Kendo grid выполняются через `DataSourceRequest`/`ToDataSourceResultAsync`.
- Ручной live refresh использует существующий `IProductCardExtractor`, но не делает write-side действий в БД.

## Требования

- .NET SDK 9.0.311+ (проект таргетится в `net8.0`, но в репо закреплен совместимый установленный SDK)
- PostgreSQL 16+ (или `docker compose`)

## Быстрый запуск

Подробная инструкция по двум PostgreSQL БД, выбору `Dev` / `Stage` и stage safety
policy находится в [docs/varprice-stage.md](docs/varprice-stage.md).

### 1) Поднять инфраструктуру (опционально)

```bash
docker compose up -d postgres
```

The Docker PostgreSQL container creates two databases on a clean data volume:

- `varprice` - development database, created by `POSTGRES_DB`
- `varprice_stage` - stage database, created by `db/init/001__create_stage_database.sql`

PostgreSQL runs files from `/docker-entrypoint-initdb.d` only when the data
directory is initialized for the first time. If `var_pg_data` already exists,
the new init script will not be replayed automatically. To verify the current
databases:

```bash
docker exec var_postgres psql -U var -d postgres -c "\l varprice*"
```

The application selects the target database explicitly through
`Database:Target`:

- `Dev` uses `ConnectionStrings:PostgresDev` and must point to `varprice`
- `Stage` uses `ConnectionStrings:PostgresStage` and must point to `varprice_stage`

Invalid targets, missing connection strings, or a target/connection-string
database mismatch fail at startup. The selected target and database name are
written to the startup logs.

When `Database:Target` is `Stage`, startup schema bootstrap is skipped by
default. This prevents automatic schema changes and legacy cleanup from running
against `varprice_stage`. To intentionally apply schema bootstrap to Stage,
set `Database:AllowStageSchemaBootstrap=true` for that run only after reviewing
the schema change.

Any operation that can change schema, clean data, seed, reset, backfill, or run a
batch mutation must pass through the safety policy before touching the database.

### 2) Запустить Web

```bash
dotnet run --project VarPrice.Web
```

Health endpoint: `http://localhost:8080/health` (в Docker) или локальный порт Kestrel.

### 3) Запустить Worker

```bash
dotnet run --project VarPrice.Worker -- --once --job vegetables
```

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
- при `Database:Target=Stage` destructive/dev-only operations must be blocked by application guards
- перед запуском схема и DB routines уже должны быть применены

Запуск:

- открой `db/seeds/001__local_debug_month.sql` в `DataGrip` и выполни его против локальной БД `varprice`
- после завершения script сам вернет summary counts по основным таблицам и статусам

## Параметры командной строки (Worker)

Поддерживаются параметры:

- `--once`
- `--job <name>`

### `--once`

Флаг наличия параметра:

```csharp
var once = args.Contains("--once");
```

Если флаг указан, приложение завершится с кодом:

- `0`, если `result.Status == "ok"` (без учета регистра)
- `1`, если статус не `ok`

Примечание: в текущей реализации Worker возвращает те же коды завершения даже без `--once`.

### `--job <name>`

Индекс параметра в аргументах:

```csharp
var jobIndex = Array.IndexOf(args, "--job");
```

Поведение:

- если `--job` передан и после него есть значение, берется это значение
- если не передан, используется значение по умолчанию: `vegetables`
- если значение не `vegetables`, Worker пишет `Unsupported job: <name>` и завершается с кодом `2`

Примеры:

```bash
dotnet run --project VarPrice.Worker
dotnet run --project VarPrice.Worker -- --once
dotnet run --project VarPrice.Worker -- --job vegetables
dotnet run --project VarPrice.Worker -- --once --job vegetables
```

## Коды завершения Worker

- `0` - успешный run (`status=ok`)
- `1` - run завершился с ошибочным статусом
- `2` - передан неподдерживаемый `--job`

## Конфигурация

Основные ключи (`appsettings.json`):

- `Database:Target` (`Dev` или `Stage`)
- `Database:AllowStageSchemaBootstrap` (default `false`)
- `ConnectionStrings:PostgresDev`
- `ConnectionStrings:PostgresStage`
- `Crawler:SitemapIndexUrl`
- `Crawler:VegetablesUrlContains`
- `Crawler:MaxProductsPerRun`
- `Crawler:MaxUrls`
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

Переопределение через переменные окружения:

- `Database__Target`
- `Database__AllowStageSchemaBootstrap`
- `ConnectionStrings__PostgresDev`
- `ConnectionStrings__PostgresStage`
- `Crawler__SitemapIndexUrl`
- `Crawler__VegetablesUrlContains`
- `Crawler__MaxProductsPerRun`
- `Crawler__MaxUrls`
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

Пример переключения Web на stage-базу:

```powershell
$env:Database__Target = "Stage"
$env:ConnectionStrings__PostgresStage = "Host=localhost;Port=55432;Database=varprice_stage;Username=var;Password=myPassword"
dotnet run --project VarPrice.Web
```

Пример переключения Worker на stage-базу:

```powershell
$env:Database__Target = "Stage"
$env:ConnectionStrings__PostgresStage = "Host=localhost;Port=55432;Database=varprice_stage;Username=var;Password=myPassword"
dotnet run --project VarPrice.Worker -- --once --job vegetables
```

Коды ошибок crawler, сохраняемые в `crawl_error.error_code`:

- `not_found`
- `too_many_requests`
- `timeout`
- `http_5xx`
- `parse_failed`
- `unknown`

## Модель хранения результатов обхода

- `crawler_run` хранит именно журнал конкретных запусков crawler, а не справочник crawler-ов.
- `crawler_run.status` хранится как `varchar(32)` со значениями `running`, `ok`, `error`.
- `product` нормализован и использует внутренний PK `product.id`; внешний идентификатор хранится отдельно в `product.external_id`.
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
- `price_observation_store` инкапсулирует единое доменное действие записи observation:
  поиск existing product, upsert `product`, чтение latest snapshot,
  проверку meaningful change, conditional insert `price_snapshot`
  и возврат `(productId, snapshotId, snapshotCreated)`.
- Общие SQL helper-объекты для будущих routines допускают префикс `routine_support_*`.
- `schema.sql` и `db/routines/**/*.sql` копируются в output/publish для `VarPrice.Web` и `VarPrice.Worker`,
  поэтому bootstrap работает как из репозитория, так и из опубликованного приложения.

## Integration tests for DB routines

- Ключевые write-side сценарии покрыты в `VarPrice.Web.Tests/WorkerIntegrationTests.cs`.
- Тесты проверяют:
  `crawler_run`, `ingestion_run`, `price_observation_store`,
  `crawl_error_add`, queue lifecycle, reaper, stats и полную use-case интеграцию.

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
dotnet msbuild VarPrice.Application/VarPrice.Application.csproj -t:GetBuildVersion -getProperty:Version
dotnet msbuild VarPrice.Application/VarPrice.Application.csproj -t:GetBuildVersion -getProperty:AssemblyVersion
dotnet msbuild VarPrice.Application/VarPrice.Application.csproj -t:GetBuildVersion -getProperty:FileVersion
dotnet msbuild VarPrice.Application/VarPrice.Application.csproj -t:GetBuildVersion -getProperty:AssemblyInformationalVersion
```

## Тесты

```bash
dotnet test VarPrice.sln
```

Focused checks for the two-database setup:

```bash
dotnet test VarPrice.Web.Tests/VarPrice.Web.Tests.csproj --filter TargetDatabaseResolverTests
dotnet test VarPrice.Web.Tests/VarPrice.Web.Tests.csproj --filter StageSafetyGuardTests
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
