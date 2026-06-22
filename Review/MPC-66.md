# MPC-66 — Резюме выполненной работы

## Реализовано

- `crawler_run` расширен типом запуска, discovery source, длительностью, структурированными counters и сведениями об ошибке.
- Добавлена `crawler_run_stage` с ограничениями, уникальностью этапа и индексом по run.
- Добавлены domain read models, стабильные run/stage constants и `ICrawlerRunReadRepository`.
- Реализован thread-safe `CrawlerRunMetrics`; статистика сохраняется агрегированно, без per-product updates.
- Catalog refresh сохраняет discovered/accepted/inserted/updated/reactivated/deactivated и stage timings.
- Price collection сохраняет финальные queue states, фактические product/snapshot/error writes и stage timings.
- Result use case, structured logs и Worker summary используют единый набор counters.
- Worker поддерживает `run-all` и выводит два независимых summary.
- Добавлен read-only API: recent list, details и aggregate statistics.
- README и `docs/architecture.md` обновлены.

## База данных

Bootstrap и routines применены только к `varprice_test`. Рабочая база `varprice` не изменялась.

Новый routine `crawler_run_complete` сохраняет counters и JSON batch stages за один вызов. Read routines поддерживают
details, recent filters/limit и aggregate диапазон. Добавлены constraints неотрицательности, корректности дат и `run_type`,
а также индексы по started/status/type.

## Проверка

- `dotnet build VarPrice.sln --no-restore`: успешно, 0 ошибок.
- Полный suite: 208 passed, 0 failed (`VarPrice.Web.Tests` 187; `VarPrice.Worker.Tests` 21).
- Controlled catalog (`varprice_test`): RunId 1, duration 43 ms, discovered 2, accepted 2, inserted 2, updated 0,
  reactivated 0, deactivated 0.
- Worker collect-prices со stub и batch=2 (`varprice_test`): RunId 2, selected 2, enqueued 2, succeeded 2,
  retry 0, dead 0, products created 2, products updated 0, snapshots 2, errors 0.
- API smoke: recent=2; details RunId=2, succeeded=2, stages=4; aggregate price runs=1, succeeded=2.

## Ограничения

Dashboard UI, Prometheus/Grafana/OpenTelemetry, alerts, retention policy и scheduler не входят в MPC-66 и не реализованы.
NuGet vulnerability audit выводит NU1900 при недоступности `https://api.nuget.org/v3/index.json`; сборка и тесты проходят.
