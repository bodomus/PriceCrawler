# MPC-61 — Добавить постоянный каталог товаров

## Summary

Добавлен постоянный каталог обнаруженных товарных URL `product_catalog` без подключения к текущему runtime crawler flow. Реализованы domain-модели, repository-интерфейс, PostgreSQL repository, DB routines для batch upsert и чтения, DI-регистрация, документация и тесты.

## Files changed

- `VarPrice.Domain/Models/ProductCatalogItem.cs` — immutable модель записи каталога.
- `VarPrice.Domain/Models/ProductCatalogUpsertItem.cs` — input-модель batch upsert.
- `VarPrice.Domain/Models/ProductCatalogUpsertResult.cs` — результат batch upsert.
- `VarPrice.Domain/Interfaces/IProductCatalogRepository.cs` — repository contract.
- `VarPrice.Infrastructure/Persistence/ProductCatalogBatchPreparer.cs` — trim, validation и deterministic deduplication входного batch.
- `VarPrice.Infrastructure/Persistence/PgProductCatalogRepository.cs` — PostgreSQL implementation через `PgRoutineExecutor` и `DbRoutineCall`.
- `VarPrice.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` — DI registration.
- `VarPrice.Infrastructure/Properties/AssemblyInfo.cs` — доступ тестов к internal batch preparer.
- `schema.sql` — таблица `product_catalog`, constraints и индексы.
- `db/routines/040__product_catalog_routines.sql` — DB routines каталога.
- `VarPrice.Web.Tests/ProductCatalogRepositoryTests.cs` — unit и PostgreSQL integration tests.
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
- `dotnet test --no-build` — остановлен по таймауту через 184 секунды без вывода.
- `dotnet test --no-build --filter ProductCatalogRepositoryTests --logger "console;verbosity=normal"` — остановлен по таймауту через 184 секунды, потому что PostgreSQL/Docker недоступны.
- `docker ps` — не смог подключиться к Docker daemon.
- Быстрые unit-тесты `ProductCatalogRepositoryTests` для empty/invalid/duplicate/trimming прошли: 4 passed.
- `Test-NetConnection localhost:55432` — TCP connect к PostgreSQL test port не прошел.

## Architecture notes

- Domain не зависит от Infrastructure/PostgreSQL/Npgsql.
- SQL не добавлен в Application, Worker, Web controllers или use cases.
- Runtime crawler flow не изменен.
- `product_catalog` не заменяет `product`; `product` остается сущностью extracted product cards и price snapshots.
- Подключение discovery к `product_catalog` относится к следующим тикетам MPC-62/MPC-63.

## Risks and limitations

- PostgreSQL integration tests добавлены, но не были фактически выполнены в этом окружении из-за недоступного Docker/PostgreSQL на `localhost:55432`.
- Ручные SQL-проверки `select * from product_catalog order by id;` не выполнены по той же причине.
- Перед merge желательно поднять test database и выполнить полный `dotnet test --no-build`.

