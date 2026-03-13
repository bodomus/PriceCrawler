# crawler_run and snapshots

## Что такое `crawler_run`

`crawler_run` это журнал конкретных запусков crawler.

- Одна запись = один запуск.
- Таблица не является справочником crawler-ов.
- Поле `status` хранится как `smallint` с ограничением `check (status in (0,1,2))`.

Соответствие enum:

```csharp
public enum RunStatus
{
    Running = 0,
    Ok = 1,
    Error = 2
}
```

Основные поля `crawler_run`:

- `run_id`
- `started_at`
- `finished_at`
- `status`
- `source`
- `note`

Индекс для выборок по источнику и последним запускам:

- `crawler_run(source, started_at desc)`

## Что такое append-only `price_snapshot`

`price_snapshot` хранит только значимые изменения состояния товара.

Это append-only таблица:

- существующие записи не переписываются;
- `captured_at` у старой записи не обновляется;
- новый snapshot создается только когда состояние реально изменилось.

Поля `price_snapshot`:

- `snapshot_id`
- `run_id`
- `captured_at`
- `product_key`
- `city`
- `regular_price`
- `final_price`
- `discount_percent`
- `promo_flag`
- `in_stock`
- `queue_id`

Индексы:

- `price_snapshot(product_key, captured_at desc)`
- `price_snapshot(run_id)`

`queue_id` в `price_snapshot` не уникален. Мы не фиксируем жесткую модель
`one queue item = one snapshot`, пока это явно не доказано.

## Когда создается snapshot

Новая запись в `price_snapshot` создается, если товар имеет минимально валидное состояние:

- известен `product_key`;
- и заполнено хотя бы одно из:
  `regular_price`, `final_price`, `in_stock`.

После этого snapshot создается только если изменилось хотя бы одно поле:

- `regular_price`
- `final_price`
- `discount_percent`
- `promo_flag`
- `in_stock`

Если товар новый:

1. создается `product`;
2. при наличии минимально валидного состояния создается первый `price_snapshot`;
3. обновляется `product.last_seen_at`.

## Когда обновляется только `product.last_seen_at`

Если товар успешно обработан, но состояние не изменилось, новый snapshot не создается.

Вместо этого обновляется только:

- `product.last_seen_at`

Это отделяет событие "товар снова увидели" от события "состояние товара изменилось".

## Как работают `product_errors`

`product_errors` сохраняет ошибки с полным контекстом обработки товара.

Поля:

- `product_error_id`
- `run_id` - обязательно
- `product_key` - nullable
- `price_snapshot_id` - nullable
- `queue_id` - nullable
- `occurred_at`
- `stage`
- `error_code`
- `error_message`
- `details_json`

Индексы:

- `product_errors(run_id)`
- `product_errors(product_key)`

Правила записи:

- Если ошибка некритическая и удалось собрать валидное состояние товара, может быть создан snapshot,
  затем ошибка сохраняется в `product_errors` со ссылкой на `price_snapshot_id`.
- Если ошибка критическая и валидный snapshot собрать нельзя, snapshot не создается,
  сохраняется только запись в `product_errors`.
- Для транзиентных retry в очередь сохраняется контекст последней ошибки в `price_collect_queue`,
  а финальная запись в `product_errors` фиксирует невосстановимую ошибку обработки.
