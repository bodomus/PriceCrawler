# MPC-80 — отчёт о выполнении

## Ticket

`MPC-80 — Disable automatic schema mutation in Stage and Production`

Ветка: `Codex/MPC-80`, создана от завершённой `Codex/MPC-79` (`4cb787fcb18af58f27f1aa55bfd1cc3e4c588a28`). Рабочее дерево перед изменениями MPC-80 было чистым.

## Результат

Web и Worker больше не используют универсальный boolean-controlled schema ensure. Введён один явный режим:

```csharp
public enum DatabaseSchemaStartupMode
{
    ValidateOnly,
    Ensure
}
```

Итоговая политика:

| Окружение | Режим | Поведение |
|---|---|---|
| Development | `Ensure` | baseline для пустой БД или approved legacy ensure для существующей, затем обязательная validation |
| Test | `Ensure` | детерминированная и повторяемая инициализация, затем обязательная validation |
| Stage / Staging | `ValidateOnly` | только чтение `schema_version` |
| Production | `ValidateOnly` | только чтение `schema_version` |
| неизвестное | `ValidateOnly` | safe fallback; `Ensure` запрещён |

Hard guard проверяет итоговое значение после всех configuration providers. `Ensure` вне Development/Test приводит к исключению до открытия соединения и до вызова initializer. Environment variable и Web command line не могут обойти правило.

> Stage and Production schema changes belong to deployment, not application startup.

## Workflow

- Level: 2 — structural/operational database startup change.
- Graphify: полностью обновлён до и после реализации.
  - pre-change: 242 файла, 1 990 узлов, 4 821 связь;
  - post-change: 253 файла, 2 067 узлов, 5 019 связей.
- CRG: stale preflight metadata обнаружена и исправлена полным structural rebuild.
  - post-change snapshot: 98 087 узлов, 568 075 связей, 271 tracked-файл;
  - FTS: 98 087 записей;
  - impact относительно `Codex/MPC-79`: 27 tracked-файлов, 30 изменённых symbols, risk `0.60`, неожиданных affected flows нет.
- Полная CRG flow/community post-processing дважды не завершилась за 5 минут. Structural graph и FTS записаны успешно; новый untracked-код дополнительно полностью покрыт Graphify, прямым source review, компиляцией и тестами. CRG MCP transport после preflight rebuild был недоступен.
- Preflight-материалы:
  - `Tickets/MPC-80-investigation.md`;
  - `Tickets/MPC-80-schema-startup-paths.md`;
  - `Tickets/MPC-80-implementation-plan.md`.

## Архитектура

```text
Web / Worker
  -> DatabaseSchemaStartupCoordinator
     -> DatabaseSchemaStartupPolicy (до доступа к БД)
     -> Ensure
        -> DatabaseSchemaInitializer
           -> empty Dev/Test: 0001_baseline.sql
           -> existing Dev/Test: SchemaBootstrapper
        -> DatabaseSchemaValidator
           -> DatabaseSchemaVersionReader (read-only)
     -> ValidateOnly
        -> DatabaseSchemaValidator
           -> DatabaseSchemaVersionReader (read-only)
```

Ответственности разделены:

- `DatabaseSchemaStartupPolicy` — non-bypassable environment guard.
- `DatabaseSchemaInitializer` — единственный новый runtime-владелец mutation path.
- `DatabaseSchemaValidator` — интерпретация read-only результата и actionable errors.
- `DatabaseSchemaVersionReader` — только два SELECT-запроса к metadata.
- `DatabaseSchemaStartupCoordinator` — выбор режима, последовательность и structured logging.
- `DatabaseSchema.ExpectedVersion` — единственный источник ожидаемой версии (`1`).

## Удалённые и заменённые пути

Удалено:

- `DatabaseSchemaStartupService`;
- `DatabaseSchemaOptions.AllowAutomaticInitialization`;
- `DatabaseSchemaOptions.ValidateOnStartup`;
- молчаливое игнорирование unsafe `Ensure` в protected environment;
- generic log `Schema ensured`;
- Worker log `Worker command started` до schema validation.

Web и Worker не содержат прямых ссылок на `SchemaBootstrapper`, baseline, bootstrap или `EnsureSchemaAsync`. Оба вызывают только `DatabaseSchemaStartupCoordinator.ExecuteAsync()`.

Прямые вызовы `SchemaBootstrapper` остались только в существующих PostgreSQL integration tests для подготовки тестовой схемы.

## Read-only гарантия Stage и Production

`ValidateOnly` не имеет ссылки на initializer или bootstrapper. Validator вызывает только `DatabaseSchemaVersionReader`, который выполняет:

```sql
select to_regclass('public.schema_version') is not null;
select max(version) from public.schema_version;
```

Гарантия проверена четырьмя независимыми способами:

1. До и после Stage/Staging/Production validation совпадают количества schema objects, routines, metadata и прикладных строк.
2. Production validation успешно выполняется login-ролью без `CREATE`/DDL permission и только с `USAGE` + `SELECT schema_version`.
3. Пустая Stage БД после failure остаётся пустой: `schema_version` и application tables не создаются.
4. БД, содержащая только корректную metadata version 1, не получает отсутствующие application tables и не repair-ится.

Baseline, bootstrap, migrations и repair не вызываются в `ValidateOnly`.

## Startup gating

- Web вызывает coordinator после построения service provider, но до `app.Run()`. При validation failure listener не открывается.
- Worker вызывает coordinator после `host.Build()`, но до command-start log, создания run scope, разрешения use case, чтения очереди или crawler work.
- Schema startup exception не перехватывается как успешный результат; оба процесса завершаются с non-zero exit code.

Actual-process integration tests подтвердили:

- Stage Web с отсутствующей metadata завершился non-zero, строка `Now listening on` отсутствовала, TCP port был закрыт;
- Stage Worker с version 0 завершился non-zero, `Worker command started` отсутствовал, `crawler_run` не изменился;
- Production Web с `DatabaseSchema__StartupMode=Ensure` завершился до database access и не создал объектов.

## Configuration precedence

1. `appsettings.json` — safe `ValidateOnly` fallback.
2. `appsettings.<Environment>.json` — environment intent.
3. Environment variables — override JSON.
4. Web command-line configuration — более высокий precedence.
5. Worker operational CLI не передаётся Generic Host (`Args = []`), поэтому не становится скрытым config override.
6. Tests могут добавить in-memory provider последним.

Hard guard применяется после получения effective mode. Более высокий provider может вызвать безопасный startup failure, но не может включить mutation в Stage/Production.

Добавлены конфигурации `Development`, `Test`, `Stage`, `Staging`, `Production` для обоих хостов. Ранее отсутствовавший `appsettings.Stage.json`, который ожидает `deploy-stage.ps1`, теперь входит в Web и Worker output/publish.

## Empty Development/Test initialization

Пустая Development/Test БД создаётся из versioned `db/migrations/0001_baseline.sql`, а не из невёрсионированного repair-пути. После baseline восстанавливается session `search_path`; это исправляет найденную тестами проблему повторного Ensure на том же connection. Повторный Test Ensure сохраняет прикладные строки и version 1.

Schema version `2` не вводилась. Baseline и bootstrap MPC-79 семантически не изменялись. Downgrade отсутствует.

## Logging и ошибки

Structured messages содержат:

- `Environment`;
- `SchemaStartupMode`;
- `ExpectedSchemaVersion`;
- `ActualSchemaVersion`;
- `Result`;
- `Reason` для failure.

Различаются unsafe configuration, missing table, empty metadata, older version, newer version и initialization/validation error. Ошибки содержат operator action и не включают connection string, password или token.

## Проверки

| Проверка | Результат |
|---|---|
| `dotnet restore PriceCrawler.sln` | успешно, всё актуально |
| `dotnet build PriceCrawler.sln` | успешно, 0 warnings, 0 errors |
| `dotnet build PriceCrawler.sln -c Release --no-restore` | успешно, 0 warnings, 0 errors |
| Schema/config/process/release focused tests | 53/53 успешно до последнего добавленного no-repair case; финальный полный suite включает его |
| `WorkerIntegrationTests` | 23/23 успешно |
| `dotnet test PriceCrawler.sln -c Release --no-build` | 303/303: Web 282, Worker 21 |
| Development Web smoke | `Ensure`, expected=actual=1, HTTP 200 |
| Stage Web smoke | `ValidateOnly`, expected=actual=1, HTTP 200 |
| Failed Web/Worker process gating | успешно |
| Runtime role without DDL | Production validation успешно |
| Release packaging | успешно |
| `git diff --check` | ошибок нет |
| Temporary PostgreSQL databases/roles after tests | 0 / 0 |

Release archive:

`artifacts/release/PriceCrawler-v0.4.1-alpha.zip` — 51 152 198 байт.

Проверено наличие:

- root `db/migrations/0001_baseline.sql`;
- root `db/scripts/bootstrap-schema-version.sql`;
- Web/Worker `appsettings.Stage.json` и `appsettings.Test.json`;
- Web/Worker copy `db/migrations/0001_baseline.sql`;
- `release.json` с `minimumSchemaVersion=1`, `targetSchemaVersion=1`.

Development БД после smoke:

`1 | 0001_baseline | v0.4.1-alpha`.

Stage и Production базы не подключались и не изменялись. Все protected-environment сценарии выполнялись на изолированных локальных PostgreSQL БД.

## Документация

Обновлены:

- `README.md`;
- `db/README.md`;
- `docs/database-environments.md`;
- `docs/architecture.md`;
- `scripts/howdeploy.md`;
- `Status.md`;
- `CHANGELOG.md`;
- environment configuration templates.

## Оставшиеся риски и границы

- `schema_version` остаётся trust anchor: version 1 означает, что deployment корректно применил baseline/migrations. Runtime intentionally не выполняет полный structural audit и не repair-ит отсутствующие application objects.
- CRG CLI не включает новые untracked-файлы в change-impact до их добавления в Git index; этот gap закрыт Graphify (все 253 файла), direct source inspection и исполняемыми тестами. Working tree намеренно не staged и не committed по запросу пользователя.
- Полная CRG flows/communities post-processing зависает в локальной версии инструмента; structural graph и FTS актуальны.

## Итог

Все критерии MPC-80 выполнены. Stage и Production application startup выполняют только read-only schema version validation, unsafe mutation configuration не может пройти hard guard, Web/Worker заблокированы до успешной проверки, Development/Test сохраняют явный и детерминированный initialization path.
