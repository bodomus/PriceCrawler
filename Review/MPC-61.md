# MPC-61 — Добавить постоянный каталог товаров

## Summary

Добавлен постоянный каталог обнаруженных товарных URL `product_catalog` без подключения к текущему runtime crawler flow. Реализованы domain-модели, repository-интерфейс, PostgreSQL repository, DB routines для batch upsert и чтения, DI-регистрация, документация и тесты.

## Files changed

- `PriceCrawler.Domain/Models/ProductCatalogItem.cs` — immutable модель записи каталога.
- `PriceCrawler.Domain/Models/ProductCatalogUpsertItem.cs` — input-модель batch upsert.
- `PriceCrawler.Domain/Models/ProductCatalogUpsertResult.cs` — результат batch upsert.
- `PriceCrawler.Domain/Interfaces/IProductCatalogRepository.cs` — repository contract.
- `PriceCrawler.Infrastructure/Persistence/ProductCatalogBatchPreparer.cs` — trim, validation и deterministic deduplication входного batch.
- `PriceCrawler.Infrastructure/Persistence/PgProductCatalogRepository.cs` — PostgreSQL implementation через `PgRoutineExecutor` и `DbRoutineCall`.
- `PriceCrawler.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` — DI registration.
- `PriceCrawler.Infrastructure/Properties/AssemblyInfo.cs` — доступ тестов к internal batch preparer.
- `schema.sql` — таблица `product_catalog`, constraints и индексы.
- `db/routines/040__product_catalog_routines.sql` — DB routines каталога.
- `PriceCrawler.Web.Tests/ProductCatalogRepositoryTests.cs` — unit и PostgreSQL integration tests.
- `docs/architecture.md` — ownership и planned flow.
- `README.md` — persistence entities и routines.

## Database changes

- Таблица: `product_catalog`.
- Unique index: `ux_product_catalog_source_normalized_url` по `(source, normalized_url)`.
- Scheduler index: `ix_product_catalog_due` по `(is_active, next_check_at, last_checked_at, id)`.
- Discovery age index: `ix_product_catalog_last_discovered_at`.
- Constraint: `ck_product_catalog_consecutive_errors_non_negative`.
- Routines:
  - `product_catalog_upsert_discovered`
  - `product_catalog_get_by_id`
  - `product_catalog_get_by_source_normalized_url`

## Behavior

- Пустой batch возвращает `0/0/0` и не обращается к БД.
- Перед DB call repository удаляет invalid items, trim-ит строки и дедуплицирует по `source + normalized_url` без учета регистра.
- При duplicate внутри batch выигрывает запись с самым поздним `DiscoveredAtUtc`, при равной дате — последняя запись.
- Batch upsert передается в PostgreSQL одним вызовом routine.
- Повторный discovery обновляет `last_discovered_at`, `url`, реактивирует запись и не меняет `first_discovered_at`.
- `last_checked_at`, `next_check_at`, `consecutive_errors` не меняются во время discovery upsert.
- `external_id` и `slug` не затираются `null` или whitespace.

## Validation performed

- `dotnet restore` — завершился успешно, но NuGet vulnerability feed был недоступен и выдал существующие `NU1900` warnings.
- `dotnet build --no-restore` — успешно, 0 ошибок; остались `NU1900` warnings из-за недоступного `https://api.nuget.org/v3/index.json`.
- `dotnet test --no-build --filter "Category=Unit"` — успешно, 4 passed.
- `Test-NetConnection localhost:55432` — PostgreSQL test port доступен.
- `dotnet test --no-build --filter "FullyQualifiedName~ProductCatalogRepositoryIntegrationTests"` — успешно, 10 passed.
- `dotnet test --no-build` — успешно, 121 passed.

## Architecture notes

- Domain не зависит от Infrastructure/PostgreSQL/Npgsql.
- SQL не добавлен в Application, Worker, Web controllers или use cases.
- Runtime crawler flow не изменен.
- `product_catalog` не заменяет `product`; `product` остается сущностью extracted product cards и price snapshots.
- Подключение discovery к `product_catalog` относится к следующим тикетам MPC-62/MPC-63.

## Risks and limitations

- Интеграционные тесты MPC-61 используют реальную PostgreSQL test database на `localhost:55432` и очищают данные через `truncate`.
- Для предотвращения гонок вокруг общей test database добавлена непараллельная xUnit collection `Postgres integration`.
