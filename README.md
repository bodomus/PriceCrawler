# VARUS Price Crawler

Сервис для сбора и обработки данных о товарах VARUS.

## Состав решения

- `VarPrice.Domain` - доменные сущности и контракты.
- `VarPrice.Application` - use-case и orchestration.
- `VarPrice.Infrastructure` - Postgres-репозитории, bootstrap схемы, HTTP crawler adapters.
- `VarPrice.Web` - web/API хост.
- `VarPrice.Worker` - консольный запуск crawler.

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

Переопределение через переменные окружения:

- `ConnectionStrings__Postgres`
- `Crawler__SitemapIndexUrl`
- `Crawler__VegetablesUrlContains`
- `Crawler__MaxProductsPerRun`

## Тесты

```bash
dotnet test VarPrice.sln
```
