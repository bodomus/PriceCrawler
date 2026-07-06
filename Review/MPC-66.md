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

- SQL named argument в `make_interval` проверен в raw-файле и исправлен с `secs = >` на `secs =>`.
- После последующих правок строки оператор повторно проверен: source и build-output содержат raw bytes `3D-3E`, поиск
  `secs\s+=\s+>` по SQL assets пуст, а deployed `pg_get_functiondef` подтверждает корректный `secs =>`.
- `public` schema базы `varprice_test` полностью удалена и создана заново; bootstrap с нуля успешно создал schema и routines.
- Clean-bootstrap integration test сохранил stage `queue-processing` (`duration_ms=25`, `item_count=3`), а
  `pg_get_functiondef(crawler_run_complete)` подтвердил корректный named argument.
- `crawler_run_get_by_id` возвращает явный `RETURNS TABLE(...)` projection без `select *`; C# mapper синхронизирован
  с этим стабильным порядком. Integration test проверяет identity/status/time, все ключевые counters, errors и note.
- `CrawlerRunMetrics` отвечает только за counters; измерение, validation и uniqueness stages вынесены в
  `CrawlerRunStageRecorder`. Recorder и DB read model сохраняют порядок выполнения, а не алфавитную сортировку.
- `ProductObservationWriteResult` возвращает независимые `ProductCreated`/`ProductUpdated`; no-op больше не считается
  update. DB routine определяет update по фактическому изменению business fields, а accumulator считает только явные флаги.
- Catalog failure path передаёт текущий ordered stage snapshot в persistence и application result; завершённые stages
  больше не теряются при ошибке последующего этапа.
- Typed `StartAsync` и statistics `CompleteAsync` в `ICrawlerRunRepository` обязательны для каждой реализации;
  default fallback в legacy methods удалён, поэтому неполная реализация теперь завершается compile-time ошибкой.
- Семантика `run-finalization` явно документирована как сумма application finalization (refresh/ingestion completion)
  и overhead `crawler_run_complete` (итоговые counters и подготовка batch stages).
- Read API валидирует и нормализует `runType` (`catalog-refresh`, `price-collection`, `legacy`) и `status`
  (`running`, `ok`, `error`); неизвестные значения возвращают `400`, а не неоднозначный пустой `200`.
- Worker создаёт один `ExecutionId` на invocation и помещает его в Serilog context/console/file output; `run-all`
  выводит ID и логирует оба независимых run IDs с общей correlation, не меняя DB schema.
- `dotnet build PriceCrawler.sln --no-restore`: успешно, 0 ошибок.
- Полный suite: 219 passed, 0 failed (`PriceCrawler.Web.Tests` 198; `PriceCrawler.Worker.Tests` 21).
- Controlled catalog (`varprice_test`): RunId 1, duration 43 ms, discovered 2, accepted 2, inserted 2, updated 0,
  reactivated 0, deactivated 0.
- Worker collect-prices со stub и batch=2 (`varprice_test`): RunId 2, selected 2, enqueued 2, succeeded 2,
  retry 0, dead 0, products created 2, products updated 0, snapshots 2, errors 0.
- API smoke: recent=2; details RunId=2, succeeded=2, stages=4; aggregate price runs=1, succeeded=2.

## Ограничения

Dashboard UI, Prometheus/Grafana/OpenTelemetry, alerts, retention policy и scheduler не входят в MPC-66 и не реализованы.
NuGet vulnerability audit выводит NU1900 при недоступности `https://api.nuget.org/v3/index.json`; сборка и тесты проходят.
