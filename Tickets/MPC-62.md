# MPC-62 — Добавить `RefreshProductCatalogUseCase`

## Summary

Подключить существующий discovery товарных URL к постоянному каталогу `product_catalog`.

## Scope

- Добавить `IRefreshProductCatalogUseCase`.
- Добавить `RefreshProductCatalogUseCase`.
- Добавить `RefreshProductCatalogResult`.
- Создавать `crawler_run` до discovery.
- Вызывать discovery через `IProductUrlDiscoveryService`.
- Преобразовывать URL в `ProductCatalogUpsertItem`.
- Использовать catalog source `varus`.
- Хранить discovery source в результате, логах и note запуска.
- Выполнять один batch upsert через `IProductCatalogRepository.UpsertDiscoveredAsync`.
- Не запускать сбор цен, не использовать `price_collect_queue`, не создавать `ingestion_run`.
- Разделить лимиты:
  - `MaxUrls` ограничивает discovery/catalog refresh.
  - `MaxProductsPerRun` ограничивает только price queue.
- Добавить ручной запуск:

```bash
dotnet run --project VarPrice.Worker -- catalog-refresh
```

## Required Verification

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet run --project VarPrice.Worker -- catalog-refresh
```

Все тесты и ручная проверка должны выполняться только на тестовой базе `varprice_test`.
