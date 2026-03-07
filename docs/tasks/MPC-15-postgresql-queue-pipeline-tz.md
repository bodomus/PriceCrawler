# MPC-15 — ТЗ: PostgreSQL queue pipeline для сбора цен

Issue: https://bodomus.youtrack.cloud/issue/MPC-15  
Project: `MPC` (Magazine Price Crawler (Varus))  
Дата: 2026-03-07

## Контекст
Текущий pipeline сбора цен работает без персистентной item-очереди в PostgreSQL: URL-ы обрабатываются сразу в памяти. Требуется перейти на зрелый и устойчивый подход: `PostgreSQL queue table + statuses + retry + batch reservation + idempotent writes`.

## Цель
Реализовать production-grade pipeline сбора цен с гарантией **at-least-once processing** и устойчивостью к падениям/дубликатам при параллельной работе нескольких воркеров.

## Область изменений
- `VarPrice.Domain`
- `VarPrice.Application`
- `VarPrice.Infrastructure`
- `VarPrice.Worker`
- SQL schema bootstrap (`schema.sql` + `SchemaBootstrapper`)
- Тесты (`VarPrice.Web.Tests` или отдельный test-проект)

UI/Views/Controller не менять, кроме случаев, где требуется отразить новый статус run.

## Функциональные требования

### 1) PostgreSQL queue table
Добавить таблицу очереди (например, `price_collect_queue`) для item-обработки URL.

Минимальный состав полей:
- `queue_id bigserial primary key`
- `run_id bigint not null references crawler_run(run_id)`
- `url varchar(1024) not null`
- `city varchar(128) null`
- `status varchar(32) not null` (`pending`, `reserved`, `succeeded`, `retry`, `dead`)
- `attempt int not null default 0`
- `max_attempts int not null`
- `next_attempt_at timestamptz not null default now()`
- `reserved_at timestamptz null`
- `lease_until timestamptz null`
- `reserved_by varchar(128) null`
- `idempotency_key varchar(128) not null`
- `last_error_code varchar(64) null`
- `last_http_status integer null`
- `last_error_message varchar(512) null`
- `created_at timestamptz not null default now()`
- `updated_at timestamptz not null default now()`
- `finished_at timestamptz null`

Ограничения/индексы:
- `unique(run_id, url)` для дедупликации постановки в очередь в рамках run.
- `unique(idempotency_key)`.
- Индекс на выборку кандидатов: `(status, next_attempt_at, queue_id)`.
- Индекс для stuck items: `(status, lease_until)`.

### 2) Batch reservation (конкурентно-безопасно)
Реализовать reservation батча в **одном SQL** с `FOR UPDATE SKIP LOCKED`:
- выбираются `pending/retry` где `next_attempt_at <= now()`;
- лимит `batch_size`;
- статус меняется на `reserved`, ставятся `reserved_at`, `lease_until`, `reserved_by`;
- возвращается батч для обработки.

Требование: несколько воркеров не должны резервировать один и тот же item.

### 3) Retry + backoff + dead-letter
На ошибке item должен менять статус:
- transient ошибка: `attempt += 1`; если `attempt < max_attempts` => `status='retry'`, `next_attempt_at=now()+backoff+jitter`;
- если попытки исчерпаны => `status='dead'`, `finished_at=now()`;
- non-transient ошибка (например, 404/parse_failed по бизнес-правилу) может сразу вести в `dead`.

Сохранять `last_error_code`, `last_http_status`, `last_error_message`.

### 4) Lease + reaper
Реализовать reaper для зависших reservation:
- `status='reserved' and lease_until < now()` переводить в `retry` с `next_attempt_at=now()` и сбросом `reserved_by`.
- Режим запуска: периодически в worker loop.

### 5) Idempotent writes
Обеспечить идемпотентность записи результатов item-обработки:
- `product` — уже upsert, оставить.
- `price_snapshot` — добавить уникальность и upsert (`ON CONFLICT DO UPDATE`) так, чтобы повторная обработка одного item не создавала дубликат snapshot.
- `product_errors` — связать с `queue_id` (nullable back-compat), добавить уникальный индекс по `queue_id` (partial), писать через upsert.

Итог: повторная обработка одного queue item не дублирует business-данные.

### 6) Orchestration pipeline
Новая модель выполнения run:
1. `crawler_run` + `ingestion_run` стартуют.
2. URL из sitemap фильтруются и **ставятся в queue** батч-вставкой (`ON CONFLICT DO NOTHING`).
3. Worker циклом: reserve batch -> process -> finalize item status.
4. Когда в queue по `run_id` не осталось `pending/retry/reserved` — закрыть `ingestion_run` и `crawler_run`.

### 7) Конфигурация
Добавить настройки:
- `Queue:BatchSize`
- `Queue:PollDelayMs`
- `Queue:LeaseSeconds`
- `Queue:MaxAttempts`
- `Queue:RetryBaseDelayMs`
- `Queue:RetryMaxDelayMs`
- `Queue:ReaperIntervalSeconds`

### 8) Наблюдаемость
Логи по каждому run:
- `reserved`, `succeeded`, `retry`, `dead`, `stuck_recovered`, latency по item.
- Причины dead по `error_code`.

## Нефункциональные требования
- Потокобезопасность при `N` воркерах.
- Без блокировок всей таблицы очереди.
- Все SQL параметризованы.
- Корректная работа при рестарте/падении процесса.

## Тесты (обязательно)

### Unit
- Backoff/jitter расчет.
- Классификация transient/non-transient.
- Статусные переходы item.

### Integration (Postgres/Testcontainers)
- Два параллельных воркера не берут один item.
- При падении после reservation item возвращается через reaper и обрабатывается повторно.
- Повторная обработка item не создает дубликат в `price_snapshot` и `product_errors`.
- Исчерпание попыток переводит item в `dead`.
- Run завершается `ok` при drain очереди, `error` при фатальной ошибке orchestration.

## Критерии приемки (DoD)
- Реализована queue таблица, индексы, миграция/bootstrapping.
- Реализован батч reservation через `FOR UPDATE SKIP LOCKED`.
- Реализованы retry/dead/reaper.
- Идемпотентные записи подтверждены интеграционными тестами.
- `dotnet test VarPrice.sln` проходит.
- README/architecture обновлены под новую модель.

## Ограничения
- Не ломать существующий контракт `RunCrawlerUseCase` и существующие dashboard-экраны.
- Новая логика должна быть совместима с текущими таблицами и данными.

## Рекомендованный порядок реализации
1. DDL + bootstrap.
2. Queue repository (enqueue/reserve/complete/fail/reap).
3. UseCase orchestration.
4. Idempotent persistence.
5. Worker loop + config.
6. Тесты и документация.
