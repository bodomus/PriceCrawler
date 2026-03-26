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
- `Price Chart` строится отдельным read-only payload и показывает trend, delta, диапазон, promo/in-stock coverage.
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
  - `Grids/Runs/IProductDetailsQuerySource.cs`
  - `Grids/Runs/IProductPriceHistoryQuerySource.cs`
  - `Grids/Runs/Dto/*` - DTO для JSON-контракта dashboard API
- `VarPrice.Infrastructure`
  - `Queries/Runs/RunsGridQuerySource.cs` - EF query для runs
  - `Queries/Runs/SnapshotsGridQuerySource.cs` - EF query для snapshots
  - `Queries/Runs/ProductDetailsQuerySource.cs` - карточка товара по выбранному snapshot
  - `Queries/Runs/ProductPriceHistoryQuerySource.cs` - история цен по `product_id` выбранного snapshot

### MVC маршруты

- `GET /` и `GET /Runs` - экран дашборда.
- `POST /Runs/IngestVegetables` - запуск crawler из dashboard.
- `GET /Runs/RunsGrid` - данные таблицы runs.
- `GET /Runs/SnapshotsGrid` - данные таблицы snapshots.
- `GET /Runs/ProductDetails` - карточка выбранного товара по `snapshotId`.
- `GET /Runs/ProductAnalytics` - полный payload для chart и summary analytics по `snapshotId`.
- `GET /Runs/ProductHistory` - история цены выбранного товара по `snapshotId`.
- `POST /Runs/RefreshLiveProduct` - ручной live-запрос в VARUS по `snapshotId` с comparison against stored snapshot.

Для `POST /Runs/IngestVegetables` используется anti-forgery token.

### Где теперь находится data access для dashboard

- `Web` слой не делает EF/SQL запросы для `Runs`.
- Весь доступ к данным для экрана находится в `VarPrice.Infrastructure/Queries/Runs`.
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
dotnet run --project VarPrice.Web
```

Health endpoint: `http://localhost:8080/health` (в Docker) или локальный порт Kestrel.

### 3) Запустить Worker

```bash
dotnet run --project VarPrice.Worker -- --once --job vegetables
```

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

- `ConnectionStrings:Postgres`
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

- `ConnectionStrings__Postgres`
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


## Как делать backup
docker exec var_postgres pg_dump -U var -d varprice -F c -f /backups/varprice.backup

## Если нужен SQL-дамп
docker exec var_postgres pg_dump -U var -d varprice -f /backups/varprice.sql

## Как восстановить
Из backup-формата:
docker exec -i var_postgres pg_restore -U var -d varprice --clean --if-exists /backups/varprice.backup

Из .sql:
docker exec -i var_postgres psql -U var -d varprice -f /backups/varprice.sql
