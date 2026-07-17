# PriceCrawler — модель окружений баз данных

## 1. Назначение документа

Этот документ фиксирует правила использования баз данных PriceCrawler в окружениях:

- Development;
- Test;
- Stage;
- Production.

Цель документа — исключить неоднозначность при разработке, тестировании и деплое, а также определить единый порядок изменения структуры и данных.

---

## 2. Общие принципы

1. Каждое окружение использует отдельную базу данных.
2. Структура базы данных движется только вперёд.
3. Downgrade схемы базы данных не поддерживается.
4. Development является источником актуальной структуры.
5. Test синхронизируется со структурой Development.
6. Stage используется для проверки release-кандидата.
7. Production содержит рабочие данные и изменяется только контролируемым deployment-процессом.
8. Изменения структуры Stage и Production не выполняются приложением автоматически при старте.
9. Перед изменением Stage и Production должна создаваться резервная копия.
10. Production никогда не заменяется копией Development после первоначального создания.

---

## 2.1. Режим запуска схемы приложения

Web и Worker используют один общий параметр:

```json
{
  "DatabaseSchema": {
    "StartupMode": "ValidateOnly"
  }
}
```

Поддерживаются только два режима:

- `Ensure` — разрешён исключительно для Development и Test. Пустая база создаётся из `0001_baseline.sql`; существующая разрешённая Dev/Test-база проходит legacy ensure; после этого версия обязательно проверяется.
- `ValidateOnly` — выполняет только read-only чтение `schema_version` и точное сравнение с `DatabaseSchema.ExpectedVersion`.

Матрица по умолчанию:

| Окружение | Режим |
|---|---|
| Development | `Ensure` |
| Test | `Ensure` |
| Stage / Staging | `ValidateOnly` |
| Production | `ValidateOnly` |

Hard guard применяется к итоговому значению после `appsettings.json`, environment-specific JSON, переменных окружения, Web command line и test overrides. Если Stage, Staging, Production или неизвестное окружение получает `Ensure`, процесс завершается до обращения к базе. Молчаливое переключение режима и отключение validation не допускаются.

Web проверяет схему до открытия HTTP-порта. Worker проверяет схему до запуска команды, получения очереди или crawler job.

> Stage and Production schema changes belong to deployment, not application startup.

---

# 3. Development

## 3.1. Назначение

Development — основная рабочая база разработчика.

Она используется для:

- разработки новых функций;
- изменения структуры таблиц;
- добавления индексов;
- изменения представлений и SQL routines;
- разработки миграций;
- проверки работы приложения на актуальных данных;
- подготовки будущих изменений для Stage и Production.

## 3.2. Кто имеет право изменять

Development могут изменять:

- разработчик;
- приложение в Development-режиме;
- локальные SQL-скрипты;
- Codex — только по явно поставленной задаче.

## 3.3. Допустимые изменения

В Development разрешены:

- создание и удаление таблиц;
- изменение колонок;
- изменение индексов;
- изменение constraints;
- изменение функций и процедур;
- преобразование данных;
- очистка отдельных рабочих данных при необходимости;
- экспериментальные изменения до их фиксации в миграциях.

## 3.4. Источник структуры

Development является главным источником текущей и будущей структуры базы данных.

Любое изменение, которое должно попасть в Stage или Production, после проверки должно быть оформлено в виде отдельной forward migration.

## 3.5. Данные

Данные Development считаются актуальными рабочими данными разработчика.

Они могут использоваться как источник для первоначального заполнения Stage и Test.

---

# 4. Test

## 4.1. Назначение

Test — изолированная база для автоматических тестов и работы Codex.

Она используется для:

- integration tests;
- database tests;
- проверок миграций;
- тестирования destructive-операций;
- временных экспериментов;
- воспроизводимых тестовых сценариев.

## 4.2. Кто имеет право изменять

Test могут изменять:

- автоматические тесты;
- Codex;
- CI;
- разработчик;
- вспомогательные test-скрипты.

## 4.3. Политика данных

Данные Test не являются ценными и могут:

- очищаться;
- пересоздаваться;
- заменяться;
- генерироваться заново;
- сбрасываться перед каждым тестовым прогоном.

## 4.4. Источник структуры

Структура Test должна соответствовать Development.

Обновление выполняется:

- пересозданием базы по актуальному schema baseline;
- применением всех актуальных миграций;
- клонированием структуры Development без переноса рабочих секретов;
- специальным test bootstrap-скриптом.

Test не является источником структуры для других окружений.

---

# 5. Stage

## 5.1. Назначение

Stage — окружение проверки release-кандидата перед Production.

Оно используется для:

- проверки готового release-пакета;
- smoke tests;
- проверки миграций;
- проверки Web и Worker;
- проверки конфигурации окружения;
- проверки совместимости приложения и схемы;
- проверки поведения на реалистичных данных.

## 5.2. Кто имеет право изменять

Stage изменяется только через контролируемый deployment-процесс.

Допустимые источники изменений:

- `deploy-stage.ps1`;
- SQL migrations из release-пакета;
- явная административная операция разработчика;
- специальный режим refresh из Development.

Приложение Stage не должно автоматически изменять структуру при старте.

## 5.3. Источник структуры

На первом этапе Stage может создаваться из Development.

Разрешённая схема:

```text
Development
    ↓ dump/restore
Stage
```

Для копирования следует использовать PostgreSQL-инструменты:

- `pg_dump`;
- `pg_restore`;
- `psql`.

Нельзя копировать физические файлы работающего PostgreSQL-кластера.

## 5.4. Данные

Stage может содержать копию актуальных Development-данных.

Stage-данные не считаются Production-данными и могут быть пересозданы.

Однако обычный Stage deploy не обязан каждый раз полностью пересоздавать базу.

Должны поддерживаться два режима:

### Обычный deploy

- сохраняет текущую Stage-базу;
- создаёт backup;
- применяет недостающие migration-скрипты;
- обновляет приложение;
- выполняет health check.

### Refresh from Development

- создаёт backup текущей Stage-базы;
- пересоздаёт Stage из Development;
- применяет недостающие миграции;
- запускает release;
- выполняет smoke test.

## 5.5. Ограничения

Stage не должен:

- изменять Production-базу;
- использовать Production connection string;
- выполнять destructive-операции вне deployment-скрипта;
- автоматически выполнять schema ensure при старте приложения;
- принимать downgrade схемы.

---

# 6. Production

## 6.1. Назначение

Production — основная рабочая база PriceCrawler.

Она содержит:

- полный объём рабочих данных;
- историю цен;
- crawler runs;
- очереди;
- snapshots;
- product catalog;
- служебные данные production-процессов.

## 6.2. Кто имеет право изменять

Production может изменяться только:

- Production Web и Worker в рамках штатной бизнес-логики;
- `deploy-production.ps1`;
- утверждёнными SQL migrations;
- разработчиком при аварийной или административной операции;
- DBA, если такая роль будет введена.

Прямые ручные изменения должны быть исключением и документироваться.

## 6.3. Источник структуры

Первоначально Production можно один раз создать из Development.

После этого Production становится самостоятельной базой.

Дальнейшие изменения структуры выполняются только через forward migrations.

Запрещено:

```text
Development → overwrite Production
Stage → overwrite Production
Test → overwrite Production
```

## 6.4. Данные

Production-данные являются ценными и не должны заменяться данными из других окружений.

Разрешены только:

- штатные изменения приложением;
- контролируемые data migrations;
- ручные исправления по утверждённой процедуре;
- восстановление из backup.

## 6.5. Ограничения

Production deploy не должен:

- удалять базу;
- пересоздавать базу из Development;
- очищать таблицы без отдельного утверждённого migration-скрипта;
- автоматически изменять схему при старте приложения;
- запускать alpha-пакет без явного разрешения;
- продолжать деплой после ошибки migration;
- продолжать запуск при несовместимой версии схемы.

---

# 7. Политика версий структуры

## 7.1. Общий принцип

Версия схемы базы данных должна храниться отдельно от версии приложения.

Пример:

```text
Application version: v0.4.1-alpha
Database schema version: 1
```

Один release может:

- не содержать изменений схемы;
- содержать одну migration;
- содержать несколько migrations.

## 7.2. Таблица версии

Рекомендуемая таблица:

```sql
CREATE TABLE schema_version
(
    version integer NOT NULL PRIMARY KEY,
    migration_name varchar(200) NOT NULL,
    applied_at_utc timestamptz NOT NULL DEFAULT now(),
    application_version varchar(50),
    checksum varchar(128)
);
```

## 7.3. Правила migration

Каждая migration должна:

1. Иметь уникальный последовательный номер.
2. Иметь понятное имя.
3. Проверять ожидаемую предыдущую версию.
4. Выполнять изменения.
5. При необходимости преобразовывать данные.
6. Записывать новую версию в `schema_version`.
7. Выполняться в транзакции, если используемые операции это допускают.
8. Завершать deploy ошибкой при неуспешном выполнении.

Пример именования:

```text
0001_baseline.sql
0002_add_crawler_status.sql
0003_convert_existing_status_values.sql
0004_add_product_indexes.sql
```

---

# 8. Политика отсутствия downgrade

## 8.1. Основное правило

Downgrade схемы базы данных не реализуется.

Допустимое направление:

```text
1 → 2 → 3 → 4
```

Недопустимое направление:

```text
4 → 3
```

## 8.2. Причины

Downgrade может привести к:

- потере данных;
- несовместимости приложения;
- удалению новых колонок;
- нарушению constraints;
- невозможности корректно восстановить преобразованные данные.

## 8.3. Rollback приложения

Rollback приложения допускается только тогда, когда предыдущая версия приложения совместима с новой схемой.

Поэтому изменения схемы желательно выполнять по принципу expand-and-contract:

1. Добавить новую структуру.
2. Перевести приложение на новую структуру.
3. Проверить стабильность.
4. Удалить устаревшую структуру в следующем release.

---

# 9. Политика backup

## 9.1. Development

Backup Development выполняется:

- перед крупными изменениями структуры;
- перед массовым преобразованием данных;
- перед экспериментальными destructive-операциями.

## 9.2. Test

Обязательный backup Test не требуется.

## 9.3. Stage

Backup Stage обязателен перед:

- применением migrations;
- refresh из Development;
- массовым изменением данных;
- заменой release.

Backup Stage может храниться ограниченное время.

Рекомендуемое именование:

```text
pricecrawler-stage-before-v0.4.1-alpha-20260716-110000.dump
```

## 9.4. Production

Backup Production обязателен перед каждым deployment, который:

- меняет структуру;
- изменяет данные migration-скриптом;
- меняет SQL routines;
- затрагивает критические таблицы.

Production backup должен быть:

- успешно создан;
- проверен на существование;
- иметь ненулевой размер;
- сохранён вне каталога текущего release;
- не удаляться автоматически сразу после успешного deploy.

Рекомендуемое именование:

```text
pricecrawler-prod-before-v0.4.1-20260716-110000.dump
```

## 9.5. Инструменты

Для backup и restore используются:

- `pg_dump`;
- `pg_restore`;
- `psql`;
- `createdb`;
- `dropdb`.

---

# 10. Правила Stage deployment

Stage deployment выполняется только через `deploy-stage.ps1`.

Последовательность:

1. Проверить release ZIP.
2. Проверить sidecar SHA-256, прочитать и валидировать `release.json`.
3. Определить application version.
4. Проверить target schema version.
5. Создать backup Stage.
6. Остановить Stage Worker.
7. Остановить Stage Web.
8. При необходимости выполнить refresh из Development.
9. Проверить текущую schema version.
10. Применить только недостающие migrations.
11. Проверить новую schema version.
12. Распаковать release в `stage/releases/<version>`.
13. Обновить `stage/current`.
14. Подложить Stage-конфигурацию.
15. Задать Stage environment variables.
16. Запустить Stage Web.
17. Выполнить health check.
18. Запустить Stage Worker.
19. Выполнить smoke test.
20. Записать deployment log.

При любой ошибке:

- deploy останавливается;
- Worker не запускается;
- ошибка фиксируется в логе;
- автоматический downgrade схемы не выполняется;
- при необходимости база восстанавливается из backup вручную или отдельной recovery-командой.

---

# 11. Правила Production deployment

Production deployment выполняется только через `deploy-production.ps1`.

Последовательность:

1. Проверить release ZIP.
2. Проверить, что release прошёл Stage.
3. Проверить Git tag и application version.
4. Проверить допустимость типа release.
5. Создать обязательный Production backup.
6. Проверить backup.
7. Остановить Production Worker.
8. Перевести Web в maintenance mode или остановить его.
9. Проверить текущую schema version.
10. Применить только недостающие forward migrations.
11. Проверить target schema version.
12. Развернуть новую версию приложения.
13. Подложить Production-конфигурацию.
14. Запустить Web.
15. Выполнить health check.
16. Запустить Worker.
17. Проверить основные логи.
18. Проверить критические запросы и рабочие сценарии.
19. Зафиксировать результат deployment.

Production deploy должен остановиться при:

- ошибке backup;
- несовместимой версии схемы;
- ошибке migration;
- отсутствии обязательных файлов;
- ошибке запуска Web;
- неуспешном health check;
- ошибке запуска Worker.

---

# 12. Матрица ответственности

| Окружение | Основной владелец | Автоматическое изменение схемы | Можно пересоздать | Источник структуры |
|---|---|---:|---:|---|
| Development | разработчик | да | да | текущая разработка |
| Test | Codex / tests / CI | да | да | Development |
| Stage | deployment-процесс | нет | да | Development + migrations |
| Production | production deployment | нет | нет | только migrations |

---

# 13. Итоговая модель

```text
Development
    ├── источник актуальной структуры
    ├── источник Test
    └── источник первоначального/refresh Stage

Test
    └── расходуемая база для Codex и тестов

Stage
    ├── проверка release-кандидата
    ├── migrations only при обычном deploy
    └── optional refresh from Development

Production
    ├── самостоятельная рабочая база
    ├── полный объём рабочих данных
    └── только forward migrations
```

Главное правило:

> Development определяет, какой структура должна стать.  
> Migrations определяют, как Stage и Production безопасно к ней переходят.

---

# 14. Initial provisioning workflow

Первичное создание Test, Stage и Production выполняется только через `scripts/initialize-database-environments.ps1`. Полные команды, dry-run, backup/restore и secret-handling описаны в `docs/database-provisioning.md`.

- `varprice_test` пересоздаётся из `0001_baseline.sql` и по умолчанию не получает бизнес-данные Development;
- `varprice_stage` получает проверенный logical dump Development; существующий Stage заменяется только с `-ReplaceExistingStage` после проверенного backup;
- `varprice_prod` получает Development logical dump ровно один раз и только с `-ConfirmInitialProductionBootstrap`;
- после Production bootstrap сохраняется durable database-level independence marker и создаётся initial Production backup с SHA-256;
- повторный Development-to-Production bootstrap не поддерживается и блокируется по marker, `schema_version`, application objects и user tables;
- provisioning identity и runtime identity различаются; Stage/Production runtime login не должен быть superuser и не получает schema mutation rights;
- пароли не передаются параметрами скрипта и не попадают в logs/reports.

> After initial bootstrap, Production must never be replaced from Development.

После bootstrap Production изменяется только forward migrations deployment-процесса и штатной бизнес-логикой приложения.

## 14.1. Recovery после частичного bootstrap

При ошибке после начала Stage replacement или Production restore скрипт не удаляет базу автоматически. Он выводит `RECOVERY REQUIRED`, путь к проверенному Development dump и точную recovery-команду; полный порядок действий приведён в `docs/database-provisioning.md`.

- неполный Stage повторно заменяется только оператором, с `-ReplaceExistingStage` и тем же `-VerifiedDevelopmentDumpPath`; сохранённый pre-replacement backup не удаляется;
- failed initial Production database можно удалить вручную только после доказательства, что она никогда не вводилась в эксплуатацию, проверки отсутствия independence marker и подтверждения в логе, что базу создал именно неуспешный запуск скрипта;
- если marker уже существует, Production считается самостоятельной: удаление и повторный bootstrap запрещены, восстанавливается только незавершённый post-marker шаг по отдельно проверенной процедуре;
- если скрипт не создавал Production в текущем запуске, автоматическая recovery-команда удаления не формируется и требуется проверка DBA.

## 14.2. Stage/Production runtime roles

Normal Stage deployment is implemented by `Scripts/deploy-stage.ps1` and documented in `docs/stage-deployment.md`. It requires verified backup before refresh/migration, allows Development-to-Stage refresh only through an explicit switch, applies only forward migrations with the deploy identity, and then runs runtime provisioning with `-StageOnly`. Production-like targets, automatic restore, and schema downgrade are forbidden.

Normal Production deployment is implemented by `Scripts/deploy-production.ps1` and documented in `docs/production-deployment.md`. The exact ZIP must have a matching successful Stage report. The script validates the independence marker, creates and verifies a Production backup before mutation, applies only forward migrations with the separate deploy identity, runs runtime provisioning with `-ProductionOnly`, and starts Web/Worker in `ValidateOnly`. It has no Development/Stage-to-Production database-copy path and never reruns bootstrap.

После создания баз отдельный `scripts/provision-database-runtime-roles.ps1` создаёт четыре login-роли: `pricecrawler_stage_web`, `pricecrawler_stage_worker`, `pricecrawler_prod_web`, `pricecrawler_prod_worker`. Пароли читаются только из environment variables, заполняемых secret store; script parameters, repository files и logs их не содержат.

Runtime-роли не являются superuser, не имеют `CREATEDB`, `CREATEROLE`, schema/database `CREATE`, ownership или migration permissions. Они получают только application table/sequence/routine grants, необходимые текущим Web/Worker paths. Deploy/object-owner identity остаётся отдельной и единственной выполняет forward migrations и DDL.

Скрипт не выполняет baseline/bootstrap/migrations и не изменяет `schema_version`. Для каждой роли обязательны read-only проверка version `1`, успешный `ValidateOnly` startup и отрицательные проверки `CREATE TABLE`/`ALTER TABLE`. После approved forward migration provisioning grants запускаются повторно для новых объектов.
