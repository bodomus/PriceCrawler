# VARUS Price Crawler

Проект разделён на независимые сборки: домен, application workflow, инфраструктура, web и worker.

## Проекты

- `VarPrice.Domain` — сущности, enum/value objects и репозиторные контракты.
- `VarPrice.Application` — use-case `RunCrawlerUseCase`, orchestration crawler→ingestion→snapshots→statuses.
- `VarPrice.Infrastructure` — Postgres реализации репозиториев, bootstrap схемы и HTTP crawler adapters.
- `VarPrice.Web` — Razor Pages/UI и API-эндпоинты поверх use-case.
- `VarPrice.Worker` — консольный запуск crawler без web.

## Зависимости слоёв

`Web/Worker -> Application -> Domain`

`Web/Worker -> Infrastructure -> (Domain + Application ports)`

Domain не зависит от ASP.NET, Npgsql, Serilog или EF.

## Запуск Web

```bash
dotnet run --project VarPrice.Web
```

Healthcheck: `http://localhost:8080/health` (в docker) или локальный порт Kestrel.

## Запуск Worker

```bash
dotnet run --project VarPrice.Worker -- --once --job vegetables
```

Поддерживаемые аргументы:

- `--once` — выполнить один проход и завершиться с exit code.
- `--job vegetables` — запускает овощной workflow.

## Конфигурация

Общие настройки:

- `ConnectionStrings:Postgres`
- `Crawler:SitemapIndexUrl`
- `Crawler:VegetablesUrlContains`
- `Crawler:MaxProductsPerRun`

Настройки находятся в `appsettings.json` соответствующего запускаемого проекта (Web/Worker) и могут переопределяться переменными окружения.

## Тесты

```bash
dotnet test VarPrice.sln
```

Содержат unit-тесты use-case и интеграционный сценарий с PostgreSQL через Testcontainers.
