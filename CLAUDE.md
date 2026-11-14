# CLAUDE.md

Инструкции для Claude Code в репозитории VARUS PriceCrawler. Общие правила проекта — в `agents.md` (корень репозитория); этот файл их не отменяет, а дополняет текущим состоянием и договорённостями с владельцем.

## Как мы работаем

- Мы одна команда. Если владелец (bodomus) в чём-то неправ — **спорь с аргументами**, не соглашайся по умолчанию.
- Сначала факты (код, логи, БД только на чтение), потом выводы. Гипотезы называй гипотезами.
- Задачи — в YouTrack, проект **MPC**: https://bodomus.youtrack.cloud/issues/MPC
  - «Готово» = код **в master**.
  - Задачи в состоянии «Требует уточнения» не трогать — владелец их обдумывает.
  - Эпик Rust (MPC-40…50) — тестовые задачи, игнорировать.
- Работа через ветку + PR в `master`; CI (`.github/workflows/ci.yml`) должен быть зелёным. Мержит владелец.
- В коммит добавляй только файлы задачи (`git add -- <файлы>`). **Не используй `git add -A`**: в рабочей папке много неотслеживаемых локальных файлов (`.ai`, `.notes`, `third_party`, `crawler.bat`, `artifacts` и т.п.).
- Обновляй `CHANGELOG.md`, а при изменении поведения/конфигурации — `README.md`, `Status.md`, `docs/*`.
- `agents.md` требует Graphify/CRG preflight (Codex-скиллы). Если этих инструментов нет — скажи об этом и работай по исходникам (`rg`, чтение кода, тесты).

## Соглашения по коду (MPC-98)

Полностью — `docs/code-conventions.md`. Главное:

- Каждая функция — короткий XML-комментарий **на английском** (`<summary>`, `<param>`, `<returns>`, `<exception>`; для реализаций интерфейса `<inheritdoc/>`).
- Последние возможности C# 14 (file-scoped namespace, `new()`, `[]`, `switch`-выражения, первичные конструкторы, `[GeneratedRegex]`, `[LoggerMessage]`).
- Стиль из `.editorconfig` проверяется при сборке; Release — warnings as errors. В новых проектах нет XML-комментария у публичного члена — ошибка в Release.
- Старый код приводим к правилам только там, где его меняем.

## Команды

```powershell
dotnet build PriceCrawler.sln
dotnet build PriceCrawler.sln -c Release      # warnings as errors
dotnet test  PriceCrawler.sln
docker compose up -d postgres                 # контейнер var_postgres, localhost:55432
```

- Тесты с `[Trait("Platform", "Windows")]` запускают PowerShell-скрипты деплоя; в CI (Linux) они исключены фильтром `Platform!=Windows`, локально на Windows — выполняются.
- Интеграционные тесты используют `varprice_test` (`PriceCrawler.Web.Tests/PostgresIntegrationFixture.cs`), не рабочую базу.
- Worker локально: `PriceCrawler.Worker\bin\Debug\net10.0\run-dev.bat <collect-prices|catalog-refresh|vegetables|--help>` (окружение Development). Web: `dotnet run --project PriceCrawler.Web`.
- Новый Web API: `dotnet run --project PriceCrawler.Api` (http://localhost:5280/scalar). Слои, свой медиатор и как добавить сценарий — `docs/api.md`. Новые сценарии — `PriceCrawler.Application/Features/<Область>/` (запрос + обработчик + валидатор), MediatR не используем.
- Личные отладочные настройки — только в `appsettings.Local.json` (git-ignored, только Development). **Не редактируй файлы в `bin/`.**

## Базы данных (решение от 23.10.2026)

| База | Роль |
|---|---|
| `varprice` | **Рабочие данные** (ежедневный сбор цен). Фактически продакшен. |
| `varprice_stage` | **База разработки** (Development, `Ensure`). Обновляется из бэкапа `varprice`: `Scripts/refresh_stage.bat`. |
| `varprice_test` | Интеграционные тесты. |
| `varprice_prod` | Устаревшая копия от 17.07, не используется до переезда на сервер. |

- С MPC-90 строка подключения по умолчанию (`appsettings.json` Web и Worker) указывает на `varprice_stage`. К `varprice` подключается только релизный сборщик через локальный `J:\Apps\PriceCrawler\config\appsettings.Production.json`. Куда подключился запуск, видно в логе: `Database target: ...`.
- Никаких записей в `varprice` без явного согласия владельца и свежего бэкапа (`Scripts/backup_varus.bat`). Диагностика — только `set default_transaction_read_only = on`.
- Не запускай destructive-скрипты (`db/seeds/*`) и restore против `varprice`.

## Состояние на 29.10.2026

- Каталог заполнен: первый `catalog-refresh` (run 212) — 10 259 товаров за 16 мин.
- Рабочий режим сбора — **`collect-prices`** (весь каталог, `MaxProductsPerRun=11000`, ~3,2 ч, run 213). Legacy `vegetables` больше не нужен.
- Сбор идёт из релизной сборки: `Scripts/scheduled-run.ps1` (`docs/collector.md`). С MPC-91 его запускает задача Планировщика `PriceCrawler Daily Collect` (`Scripts/register-scheduled-task.ps1`): 08:00 + ежечасные повторы до 22:00 из-за частых отключений света; `-Scheduled` пропускает запуск, если сегодня уже был успешный. Старая задача `\Magazine Crawler` удаляется этим скриптом.
- Stage/Prod-деплой (эпик MPC-78, скрипты `Scripts/deploy-*.ps1`) **заморожен**: не развивать. Известные баги там (`builtAtUtc` под PowerShell 7, путь `scripts\…` в `build-release.ps1`) — не чинить без задачи.

## План (веха MPC-87 «Регулярный сбор данных отдельно от разработки»)

1. **MPC-88** — готово (каталог заполнен); открыт вопрос про 4 018 товаров в `product`, которых нет в каталоге (новинки после 24.10 и/или лимит `MaxCategoryPagesPerSeed=10`).
2. **MPC-93** — готово (PR #47): статус запуска с порогом dead-позиций (`Crawler:RunErrorDeadThresholdPercent`, 1%).
3. **MPC-89** — готово (PR #49, #50): релизный сборщик в `J:\Apps\PriceCrawler\` + `scheduled-run.ps1`, первый прогон 30.10 (run 218) успешен.
4. **MPC-90** — готово (PR #51): разработка на `varprice_stage`, `refresh-stage-from-backup.ps1`, роли баз в `docs/database-environments.md` (1.1).
5. **MPC-91** — Планировщик Windows, правило «раз в сутки» через `scheduled-run.ps1 -Scheduled` (маркер `state\last-scheduled-success.txt`), а не флаг Worker `--scheduled`; ручные запуски без ограничений.

Потом — работа над сайтом (Web) на `varprice_stage`.

## Известные вопросы (не в работе)

- У части товаров `old_price < price` (например id 5887, 5785); в июльских данных много вин с ценой 329.00 — подозрение на ошибку извлечения цены у весовых товаров. Нужна отдельная проверка.
- 344 товара без `external_id` и `external_id = 24` у двух страниц категорий — мусор до MPC-72/77.
- `crawl_error` с кодом `unknown`: 266 в октябре (последний 14.10).
- Статистика `crawler_run.*_count` не заполняется в legacy-режиме `vegetables`.
- CI не ловит уязвимые пакеты: `dotnet restore` идёт без `-c Release` (NU1902 остаётся предупреждением).
- Репозиторий публичный, при этом в нём лежат `key.snk` и dev-пароль Postgres.
