# MPC-81 — отчёт о выполнении

## Ticket

`MPC-81 — Create initial Test, Stage, and Production databases`

Ветка `Codex/MPC-81` создана от чистой `Codex/MPC-80` на commit `fd4e20de0af0`. Изменения не staged и не committed.

## Результат

Создан и фактически выполнен повторяемый initial provisioning workflow:

```text
Development varprice
    ├── baseline only ───────────────> Test varprice_test
    ├── verified logical snapshot ──> Stage varprice_stage
    └── one-time logical snapshot ──> Production varprice_prod
                                          │
                                          └── forward migrations only
```

Test, Stage и Production существуют, имеют schema version 1 и прошли независимую SQL-проверку. Production содержит durable independence marker, initial verified backup и не может быть повторно заменена Development dump.

> After initial bootstrap, Production must never be replaced from Development.

## Workflow и repository intelligence

- Workflow level: 2 — database provisioning / deployment / environment change.
- Graphify preflight/postflight: выполнен; итоговый граф — 2,122 nodes, 5,143 edges, 113 communities.
- CRG: полный structural rebuild — 279 tracked files, 141,675 raw nodes, 664,144 raw edges; status database — 98,127 normalized nodes, 568,148 edges.
- CRG FTS успешно обновлён инкрементально. Отдельный FTS-only preflight превышал 124-second timeout.
- Новые unstaged-файлы не попадают в CRG change detector даже после full build, потому что локальная версия CRG использует Git tracked-file inventory. Этот gap закрыт Graphify filesystem graph, прямой source review, PowerShell parser, PostgreSQL integration tests и реальным выполнением.
- Graph findings подтверждены в C#, SQL, конфигурации, Docker/PostgreSQL и process behavior.

Обязательные investigation/plan:

- `Tickets/MPC-81-investigation.md`;
- `Tickets/MPC-81-implementation-plan.md`.

## Инвентаризация до изменений

PostgreSQL 16 работал только в Docker:

- container: `var_postgres`;
- host/port: `localhost:55432`;
- Windows PostgreSQL service: отсутствует;
- Windows PATH: native `psql/pg_dump/pg_restore/createdb/dropdb` отсутствовали;
- Docker container содержал все необходимые PostgreSQL CLI tools.

Начальное состояние:

| Environment | Database | State до MPC-81 |
|---|---|---|
| Development | `varprice` | schema version 1, рабочие данные |
| Test | `varprice_test` | legacy tables, `schema_version` отсутствовал |
| Stage | `varprice_stage` | пустая база |
| Production | `varprice_prod` | отсутствовала |

Production data отсутствовали, поэтому one-time bootstrap не мог перезаписать ценное Production состояние. Не относящиеся к тикету базы `pricecrawler_mpc79_*` не изменялись.

## Реализация

Добавлен `scripts/initialize-database-environments.ps1`:

- `SupportsShouldProcess` / `-WhatIf`;
- независимые `-InitializeTest`, `-InitializeStage`, `-InitializeProduction`, `-InitializeAll`;
- `-ReplaceExistingTest` и `-ReplaceExistingStage`;
- обязательный `-ConfirmInitialProductionBootstrap`;
- native и explicit Docker tool modes;
- safe identifier/unique-name/source-destination guards;
- PostgreSQL tooling, connectivity, Development version/object/quiescence checks;
- expected version/application version читаются из `DatabaseSchema.cs`, не задаются configuration;
- Test создаётся из `0001_baseline.sql` без Development business data;
- Stage создаётся из consistent custom-format Development dump;
- существующий Stage обязательно backup-ится и проверяется до replacement;
- Production создаётся из того же Development snapshot ровно один раз;
- Production independence фиксируется database-level comment, не меняющим version-1 application schema;
- повторный Production bootstrap блокируется по marker, `schema_version`, application tables и user tables;
- dump/backup проверяются по non-zero size, `pg_restore --list` и SHA-256;
- critical row counts сравниваются с quiescent Development source;
- operator log/report не содержат паролей или connection strings;
- generic `-Force` и Production replacement switch отсутствуют;
- физические PostgreSQL data files не копируются.

Database-level marker выбран вместо новой `environment_metadata` table, чтобы не менять уже выпущенную схему version 1 без новой forward migration.

## Фактическое provisioning

Выполнена команда `-InitializeAll` в Docker mode с explicit Test/Stage replacement и Production confirmation.

### Итоговые базы

| Environment | Database | Schema | Business data policy |
|---|---|---:|---|
| Development | `varprice` | 1 | исходные рабочие данные, не изменялись |
| Test | `varprice_test` | 1 | baseline-only, 0 business rows |
| Stage | `varprice_stage` | 1 | Development snapshot |
| Production | `varprice_prod` | 1 | initial Development snapshot, теперь independent |

Critical row counts после provisioning:

| Table | Development | Test | Stage | Production |
|---|---:|---:|---:|---:|
| `product` | 4,990 | 0 | 4,990 | 4,990 |
| `price_snapshot` | 8,792 | 0 | 8,792 | 8,792 |
| `crawler_run` | 49 | 0 | 49 | 49 |
| `crawler_run_stage` | 0 | 0 | 0 | 0 |
| `ingestion_run` | 48 | 0 | 48 | 48 |
| `price_collect_queue` | 57,902 | 0 | 57,902 | 57,902 |
| `product_catalog` | 1 | 0 | 1 | 1 |
| `product_catalog_refresh` | 0 | 0 | 0 | 0 |
| `crawl_error` | 12,037 | 0 | 12,037 | 12,037 |

После полного test suite `varprice_test` содержала одну synthetic `product_catalog` row. Test был повторно пересоздан через guarded baseline workflow; финальное состояние снова schema version 1 и 0 business rows.

### Dump и backups

| Artifact | Bytes | SHA-256 |
|---|---:|---|
| Development bootstrap dump | 4,921,274 | `c6c5ebff9be8311a6d88eeeb83da536f9059d22f96ee7b07c8c16fd62880c7f2` |
| Stage pre-bootstrap backup | 893 | `2fb9069c4c9c3d4247cd135ab832343fc716227a73ea60a5df55aa198903cc2c` |
| Production initial backup | 4,924,646 | `0c43f535546e24113181c371285ec8c835ece2690557a4130dbc41e6bc02e7fa` |

Все три custom-format artifacts прошли `pg_restore --list`. Они находятся в ignored `artifacts/db/`; Git их не включает.

Подробный machine-generated bootstrap report: `database-environments-bootstrap-report.md`.

### Production independence

Marker содержит:

```text
environment=Production
initial_bootstrap_completed=true
initial_bootstrap_source=Development
initial_bootstrap_application_version=v0.4.1-alpha
initial_bootstrap_schema_version=1
```

Вторая Production bootstrap попытка завершилась exit code 1 с actionable refusal. До/после отказа `product` count остался 4,990. Ни generic force, ни Production replacement path не существует.

## Configuration и документация

Web/Worker environment templates теперь указывают только свои базы:

```text
Test        -> varprice_test / Ensure
Stage       -> varprice_stage / ValidateOnly
Staging     -> varprice_stage / ValidateOnly
Production  -> varprice_prod / ValidateOnly
```

Новые/обновлённые материалы:

- `config/database-environments.example.json`;
- `docs/database-provisioning.md`;
- `docs/database-environments.md`;
- `db/README.md`;
- `scripts/howdeploy.md`;
- `README.md`;
- `Status.md`;
- `CHANGELOG.md`.

Все connection strings содержат placeholders. Реальные новые credentials не добавлены.

## Validation

| Check | Result |
|---|---|
| PowerShell AST parser | success |
| JSON parsing, 11 environment/config files | success |
| Provisioning focused tests | 8/8 |
| Runtime-role provisioning tests | 2/2; four roles, host startup, DDL denial, Web/Worker separation |
| Schema/config/process/release/runtime focused tests | 64/64 |
| `WorkerIntegrationTests` | 23/23 |
| `dotnet restore PriceCrawler.sln` | success |
| Release solution build | success, 0 warnings / 0 errors |
| Full Release solution tests | 313/313: Web 292, Worker 21 |
| Release archive with runtime-role script | success; `db/scripts/provision-database-runtime-roles.ps1` present |
| Temporary Docker Test/Stage/Production integration workflow | success; cleanup confirmed |
| Recovery rerun from `-VerifiedDevelopmentDumpPath` | success; Stage restored and verified from retained dump |
| Actual database list/version/object/count verification | success |
| Actual second Production bootstrap refusal | success, exit 1, no data change |
| Stage Web `ValidateOnly` smoke | HTTP 200, expected=actual=1 |
| Production Web `ValidateOnly` smoke | HTTP 200 |
| Smoke listeners after validation | none |
| Secret scan | no real secrets added |
| `git diff --check` | no errors; only configured LF/CRLF notices |
| Temporary `pricecrawler_mpc81_*` databases | none |
| Artifact ignore rules | confirmed |

Worker не запускался с crawler-командой против Stage/Production, чтобы не создавать business runs и не выполнять live HTTP crawling. Worker использует тот же проверенный coordinator; это подтверждено existing process-gating tests, shared coordinator source inspection и environment template tests.

## Оставшиеся manual deployment steps

В PostgreSQL сейчас имеется только login/superuser `var`. Он использован как provisioning/object-owner identity и не должен стать штатным Web/Worker runtime identity. Четыре runtime password environment variables в текущей операторской сессии отсутствуют, поэтому реальные роли не создавались с вымышленными или потерянными credentials.

До реального Stage/Production application deployment необходимо:

1. заполнить четыре документированные password environment variables из внешнего secret store;
2. выполнить `scripts/provision-database-runtime-roles.ps1` и сохранить audit output;
3. передать разные Web/Worker connection strings через secret store/environment configuration;
4. не запускать Production bootstrap повторно;
5. применять будущие Production schema changes только forward migrations.

Скрипт и grants полностью реализованы и проверены на временных PostgreSQL databases. Применение к реальным Stage/Production ожидает только внешние secret values; повторный Production bootstrap для этого не используется.

## Итог

### Дополнение: recovery после частичного сбоя

После ревью добавлен штатный операторский recovery-path без ослабления guards:

- скрипт отслеживает, создавалась ли Production текущим запуском, начался ли restore и был ли записан independence marker;
- marker записывается только после успешной проверки schema version, critical objects и row counts;
- при ошибке выводятся `RECOVERY REQUIRED`, команда проверки marker, ручные команды удаления только failed initial Production и точный повторный запуск с `-VerifiedDevelopmentDumpPath`;
- Production не удаляется автоматически; если базу не создавал текущий запуск или marker уже существует, команда удаления не предлагается;
- для неполного Stage сохраняются pre-replacement backup и verified Development dump, после чего оператор может явно повторить guarded replacement;
- в документацию добавлены полная процедура и сохранение `-WhatIf`-вывода через `Tee-Object`.

### Дополнение: отдельные runtime-роли

Добавлен независимый от bootstrap скрипт `scripts/provision-database-runtime-roles.ps1` для четырёх login identities: Stage/Production Web и Worker. Credentials читаются только из четырёх secret environment variables и передаются `psql` через stdin. Роли получают безопасные `NOSUPERUSER/NOCREATEDB/NOCREATEROLE/NOINHERIT/NOREPLICATION/NOBYPASSRLS`, не владеют database/schema/application objects и не получают DDL.

PUBLIC schema/routine mutation paths закрыты; deploy/object-owner identity остаётся отдельной. Web получает allowlist crawler/read routines, Worker — operational routine catalog. Обе роли получают необходимый текущему приложению table/sequence DML, поскольку Web `IngestVegetables` использует write pipeline. Скрипт сам проверяет version `1`, отказ `CREATE TABLE` и отказ `ALTER TABLE`, не выполняя baseline, migration, bootstrap или изменение `schema_version`.

Все database-provisioning acceptance criteria, не требующие внешних runtime credentials, выполнены. Test, Stage и Production физически созданы и проверены; Test не содержит Development data; Stage/Production counts совпадают с source snapshot; Production independent, backed up и защищена от повторного Development overwrite. Schema version, baseline и runtime C# contracts не изменялись.

### Дополнение после возобновления: строгий least privilege

Повторная проверка исходного промпта выявила избыточные grants: Worker получал права на все текущие и будущие таблицы, sequences и routines, а Web — чтение всех таблиц. Реализация ужесточена:

- Web и Worker получают явные allow-list’ы таблиц, sequences и routines, соответствующие их текущим путям выполнения;
- `schema_version` доступна runtime-ролям только для чтения, `db_routine_script` недоступна;
- runtime-роли не получают default privileges на будущие объекты; после forward migration provisioning-скрипт должен быть повторно запущен с обновлёнными проверенными allow-list’ами;
- у runtime-ролей отзываются существующие memberships, чтобы исключить обход ограничений через `SET ROLE`;
- PUBLIC-доступ к application tables, sequences и routines отзывается;
- интеграционный тест дополнительно проверяет отсутствие Web-доступа к `product_catalog`, Worker-доступа к `db_routine_script` и Worker UPDATE к `schema_version`.

После изменения выполнены PowerShell AST parse, 16 unit tests, CRG incremental update и изолированный PostgreSQL integration test. Последний подтвердил provisioning четырёх ролей, успешный запуск Stage/Production Web и Worker в `ValidateOnly`, а также отказ `CREATE TABLE` и `ALTER TABLE`. Реальные Stage/Production базы и Production bootstrap не запускались; schema version осталась `1`.
