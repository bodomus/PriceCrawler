# MPC-79 — отчёт о выполнении

## Результат

В ветке `Codex/MPC-79`, созданной от `MPC-78`, введено версионирование схемы PostgreSQL с baseline-версией `1` для приложения `v0.4.1-alpha`. Web и Worker используют единый startup-контроль совместимости. Автоматические изменения схемы запрещены для Stage, Staging и Production; downgrade не реализован.

Пользовательские изменения, существовавшие до начала задачи (`.agents/skills/PRE_TICKET_WORKFLOW.md`, `.graphifyignore`, `agents.md`, `docs/database-environments.md`), сохранены и не перезаписывались.

## Предварительный анализ

- Выполнен workflow уровня 2 из `.codex/PRE_TICKET_WORKFLOW.md`.
- Graphify полностью перестроен после изменения `.graphifyignore`: 241 исходный файл, 1 986 узлов, 4 815 связей, 107 сообществ.
- CRG обновлён до и после реализации; итоговый риск `0.30`, затронутых runtime-flow не обнаружено.
- Выводы графов проверены по исходному коду, SQL и тестам.
- Подготовлены материалы исследования:
  - `Tickets/MPC-79-investigation.md`;
  - `Tickets/MPC-79-database-schema-inventory.md`;
  - `Tickets/MPC-79-implementation-plan.md`.

## Инвентаризация Development

Схема Development сопоставлена с `db/schema.sql`, шестью SQL-файлами routines и чистой Test БД. До регистрации были обнаружены:

- служебная таблица `__EFMigrationsHistory` — исключена из baseline как неиспользуемая приложением;
- устаревшая перегрузка `product_catalog_upsert_discovered(text)` — исключена из baseline;
- несовместимый индекс `ix_product_catalog_due`, в котором отсутствовал используемый текущей routine столбец `reserved_until`.

Перед исправлением создан backup контейнера:

`/backups/pricecrawler-development-before-mpc79-20260716-123808.dump` — 4 920 120 байт.

Индекс перестроен транзакционно в соответствии с текущим исходным кодом:

`is_active, next_check_at, reserved_until, last_checked_at, id`.

После повторной проверки Development зарегистрирована:

`1 | 0001_baseline | v0.4.1-alpha`.

Контрольные количества строк до и после регистрации совпали:

| Таблица | Строк |
|---|---:|
| `crawl_error` | 12 037 |
| `crawler_run` | 49 |
| `crawler_run_stage` | 0 |
| `db_routine_script` | 6 |
| `ingestion_run` | 48 |
| `price_collect_queue` | 57 902 |
| `price_snapshot` | 8 792 |
| `product` | 4 990 |
| `product_catalog` | 1 |
| `product_catalog_refresh` | 0 |

## Реализация

- Добавлен `db/migrations/0001_baseline.sql` для пустой БД: полная схема, 40 routines, метаданные шести routine-скриптов и `schema_version=1`. Повторный запуск и запуск в непустой public-схеме безопасно отклоняются.
- Добавлен `db/scripts/bootstrap-schema-version.sql` для существующей БД. Он проверяет таблицы, столбцы, типы, ограничения, критические индексы, routines и их хэши; только после успешной проверки добавляет `schema_version`. Скрипт повторяем и не изменяет прикладные данные.
- Добавлены модель ожидаемой версии, reader результата и общий `DatabaseSchemaStartupService`.
- Web и Worker переведены на общий startup-контроль.
- Development/Test могут выполнять явно разрешённую инициализацию; Stage/Staging/Production всегда работают только в режиме проверки независимо от ошибочной настройки.
- Ошибки различают отсутствие metadata, пустую, старую и более новую схему; сообщения не содержат connection string или секреты.
- Release-скрипт проверяет непрерывность миграций, совпадение версии кода и baseline, включает DB-ресурсы в ZIP и проверяет содержимое созданного архива.
- Обновлены `README.md`, `db/README.md`, `CHANGELOG.md` и `Status.md`.

## Проверка

| Проверка | Результат |
|---|---|
| `dotnet restore PriceCrawler.sln` | успешно |
| `dotnet build PriceCrawler.sln` | успешно, 0 warnings, 0 errors |
| `dotnet build PriceCrawler.sln -c Release --no-restore` | успешно, 0 warnings, 0 errors |
| Целевые schema/release/DI тесты | 18/18 успешно |
| `WorkerIntegrationTests` | 23/23 успешно |
| `dotnet test PriceCrawler.sln -c Release --no-build` | 267/267 успешно: Web 246, Worker 21 |
| SQL baseline на изолированной пустой БД | успешно; повторный запуск отклонён |
| SQL bootstrap на изолированной существующей БД | два запуска успешно, прикладная строка сохранена |
| Негативные SQL-проверки | отсутствие таблицы/столбца, неверный тип и конфликт metadata отклонены без частичных изменений |
| Web Development smoke `/health` | HTTP 200; схема 1 подтверждена |
| Release ZIP | создан и проверен |
| `git diff --check` | ошибок нет |

Архив: `artifacts/release/PriceCrawler-v0.4.1-alpha.zip`, 51 123 132 байта. В архиве присутствуют `db/migrations/0001_baseline.sql`, `db/scripts/bootstrap-schema-version.sql`, `db/README.md` и `release.json`; диапазон схемы `minimum=1`, `target=1`.

## Ограничения и риски

- Stage и Production не подключались и не изменялись. Их safety-контракт проверен на изолированных PostgreSQL БД интеграционными тестами.
- CRG CLI учитывает только отслеживаемые Git-файлы, поэтому до добавления файлов в index не связывает новый untracked DI-тест с `ServiceCollectionExtensions`; сам тест выполнен и прошёл.
- Существующие локальные строки подключения в `appsettings.json` и тестовой fixture не относятся к MPC-79 и не менялись.
- Downgrade отсутствует намеренно. Для более новой версии БД приложение завершает startup с явной ошибкой совместимости.

## Итоговая оценка

Критерии MPC-79 выполнены: определена и зафиксирована версия 1, создан baseline, существующая Development БД безопасно зарегистрирована после backup и строгой сверки, startup-проверка едина для обоих хостов, Production-подобные окружения защищены от автоматических изменений, DB-ресурсы включены в релиз, тесты и документация обновлены.
