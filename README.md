# VARUS Price Crawler

Сервис для сбора и обработки данных о товарах VARUS.

## Состав решения

- `VarPrice.Domain` - доменные сущности и контракты.
- `VarPrice.Application` - use-case и orchestration.
- `VarPrice.Infrastructure` - Postgres-репозитории, queue pipeline, bootstrap схемы, HTTP crawler adapters.
- `VarPrice.Web` - web/API хост.
- `VarPrice.Worker` - консольный запуск crawler.

## Runs Dashboard (MVC)

Экран `Runs` переведен с Razor Pages на ASP.NET Core MVC.

### Слои

- `VarPrice.Web`
  - `Controllers/RunsController.cs`
  - `ViewModels/Runs/RunsDashboardVm.cs`
  - `Views/Runs/Index.cshtml`
  - `Infrastructure/DataTables/DataTableResults.cs`
- `VarPrice.Application`
  - `Grids/Runs/IGetRunsGridQueryService.cs`
  - `Grids/Runs/IGetSnapshotsGridQueryService.cs`
  - `Grids/Runs/IGetProductsGridQueryService.cs`
  - `Grids/Runs/GetRunsGridQueryService.cs`
  - `Grids/Runs/GetSnapshotsGridQueryService.cs`
  - `Grids/Runs/GetProductsGridQueryService.cs`
  - `Grids/Runs/Dto/*` - DTO для JSON-контракта DataTables
- `VarPrice.Infrastructure`
  - `Queries/Runs/RunsGridQuerySource.cs` - EF query для runs
  - `Queries/Runs/SnapshotsGridQuerySource.cs` - EF query для snapshots
  - `Queries/Runs/ProductsGridQuerySource.cs` - SQL query для products
  - `Queries/Runs/ProductGridRow.cs` - row model SQL-проекции

### MVC маршруты

- `GET /Runs` - экран дашборда.
- `POST /Runs/RunsData` - данные таблицы runs (DataTables).
- `POST /Runs/SnapshotsData` - данные таблицы snapshots (DataTables).
- `POST /Runs/ProductsData` - данные таблицы products (DataTables).

Для всех `POST` endpoint используется anti-forgery token.

### Где теперь находится data access для grid

- `Web` слой не делает EF/SQL запросы для `Runs`.
- Весь доступ к данным для гридов находится в `VarPrice.Infrastructure/Queries/Runs`.
- Логика фильтрации/сортировки/пагинации orchestration находится в `VarPrice.Application/Grids/Runs`.

## Требования

- .NET SDK 8+
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

Коды ошибок crawler, сохраняемые в `product_errors.error_code`:

- `not_found`
- `too_many_requests`
- `timeout`
- `http_5xx`
- `parse_failed`
- `unknown`

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
