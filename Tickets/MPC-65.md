# MPC-65 — Реализовать явные режимы и команды запуска Worker

Ссылка: https://bodomus.youtrack.cloud/issue/MPC-65/MPC-65-Realizovat-yavnye-rezhimy-i-komandy-zapuska-Worker

## Задача

Реализовать для `VarPrice.Worker` явные режимы и команды запуска, чтобы оператор мог запускать нужный сценарий без неоднозначного ручного разбора аргументов.

## Ожидаемый CLI-контракт

- `vegetables [--once]` — legacy discovery + queue + price snapshot flow.
- `catalog-refresh` — обновление постоянного каталога товаров без сбора цен.
- `collect-prices` — сбор цен из постоянного каталога без discovery.
- `--help` / `-h` — справка по доступным командам и кодам завершения.

Legacy aliases должны оставаться совместимыми:

- `--job <name>`
- `--collect-prices`

## Acceptance notes

- Неподдерживаемые команды и опции завершаются контролируемо с exit code `2`.
- Конфликтующие режимы завершаются контролируемо с exit code `2`.
- Help и invalid command не должны создавать host и не должны запускать DB bootstrap.
- Существующие flows `vegetables`, `catalog-refresh`, `collect-prices` должны остаться привязанными к текущим use cases.
- Тесты должны выполняться только на тестовой базе `varprice_test`.
