# MPC-63 — Добавить CollectProductPricesUseCase и выбор товаров по oldest-first

Источник: https://bodomus.youtrack.cloud/issue/MPC-63

## Summary

Реализовать отдельный рабочий сценарий сбора цен из постоянного каталога `product_catalog`.

Use case должен:

- создать `crawler_run`;
- создать `ingestion_run`;
- выбрать из `product_catalog` ограниченную порцию активных товаров;
- выбирать в первую очередь товары, которые никогда не проверялись;
- затем выбирать товары с самым старым `last_checked_at`;
- поставить выбранные URL в существующую `price_collect_queue`;
- обработать очередь через существующий extractor;
- сохранить `product` и `price_snapshot`;
- после успеха обновить состояние записи `product_catalog`;
- после ошибки обновить счетчик ошибок и `next_check_at`;
- завершить `ingestion_run` и `crawler_run`;
- не выполнять discovery;
- не обходить category seed;
- не изменять каталог через discovery upsert.

Целевой flow:

```text
product_catalog
-> oldest-first selection
-> price_collect_queue
-> extractor
-> product / price_snapshot
-> product_catalog scheduling state update
```

## Key Requirements

- Базовая ветка: `MPC-62`.
- Рабочая ветка: `Codex/MPC-63`.
- Добавить `ICollectProductPricesUseCase`, `CollectProductPricesUseCase`, `CollectProductPricesResult`.
- Расширить `IProductCatalogRepository` методами due-selection и обновления scheduling state.
- Реализовать atomic oldest-first selection через PostgreSQL routine с `FOR UPDATE SKIP LOCKED`.
- Добавить lease-поля в `product_catalog`: `reserved_at`, `reserved_until`, `reserved_by`.
- Добавить `product_catalog_id` в `price_collect_queue` с FK на `product_catalog(id)`.
- Обновить enqueue routine так, чтобы она принимала catalog IDs.
- Добавить настройки:
  - `Crawler:CatalogLeaseSeconds`
  - `Crawler:SuccessfulCheckIntervalHours`
  - `Crawler:CatalogFailureBaseDelayMinutes`
  - `Crawler:CatalogFailureMaxDelayHours`
- Добавить worker-команду `--collect-prices`.
- `catalog-refresh` должен продолжать работать.
- Все тесты и ручные проверки проводить только на `varprice_test`.

## Acceptance Notes

Пустой due batch не является ошибкой и должен завершаться со статусом `ok`.

Retry на уровне `price_collect_queue` не должен увеличивать `product_catalog.consecutive_errors`.

Final dead failure должен увеличивать `consecutive_errors`, выставлять `last_checked_at`, `next_check_at` по backoff и очищать reservation.
