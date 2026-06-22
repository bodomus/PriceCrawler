# MPC-65 — Резюме выполнения

## Summary

Реализован явный CLI-контракт для `VarPrice.Worker`: основные режимы теперь запускаются позиционными командами `vegetables`, `catalog-refresh`, `collect-prices`, а `--help` / `-h` выводят справку без создания host и без DB bootstrap.

## Сделано

- Добавлен `WorkerCommandParser`, который централизованно разбирает команды Worker.
- Добавлены типы `WorkerRunMode` и `WorkerCommand` для явного выбора режима.
- `Program.cs` переведен с ручного поиска аргументов на результат parser-а.
- Worker CLI-аргументы больше не передаются в `Host.CreateApplicationBuilder`, поэтому позиционные команды не интерпретируются как Generic Host configuration arguments. Content root явно установлен в каталог Worker executable, чтобы `appsettings.json` продолжал загружаться при запуске через `dotnet run --project` из корня репозитория.
- Некорректные команды, неизвестные опции, отсутствующее значение `--job` и конфликтующие режимы завершаются с exit code `2`.
- `--once` оставлен как legacy/no-op только для `vegetables`; в остальных режимах parser возвращает exit code `2`.
- Сохранена обратная совместимость для legacy aliases: `--job <name>`, `--collect-prices`, запуск без аргументов как alias для `vegetables`.
- Документация в `README.md` обновлена под явные команды.
- `CHANGELOG.md` обновлен записью о новом CLI-контракте Worker.
- Добавлен отдельный проект `VarPrice.Worker.Tests` с unit-тестами `WorkerCommandParserTests`; зависимость Worker удалена из `VarPrice.Web.Tests`.

## Validation

- `dotnet build VarPrice.sln`
- `dotnet test VarPrice.Worker.Tests\VarPrice.Worker.Tests.csproj --no-build`
- `dotnet run --no-build --project VarPrice.Worker -- --help`
- `dotnet run --no-build --project VarPrice.Worker -- unknown` с проверкой exit code `2`
- `dotnet test VarPrice.sln --no-build`

Полный тестовый прогон прошел на тестовой БД `varprice_test` через существующий `PostgresIntegrationFixture`.

## Notes

- Изменений схемы БД не было.
- Рабочая БД `varprice` не использовалась для тестов.
- Markdown тикета сохранен в `Tickets/MPC-65.md` и ранее прикреплен к MPC-65 через YouTrack REST API.

## Validation исправлений 2026-06-22

- `dotnet build VarPrice.sln` — успешно; только предупреждения `NU1900` из-за недоступного NuGet vulnerability feed.
- `dotnet test VarPrice.Worker.Tests\VarPrice.Worker.Tests.csproj --no-build --no-restore` — успешно.
- `dotnet run --no-build --project VarPrice.Worker -- --help` — exit code `0`.
- Invalid command и `--once` с `catalog-refresh` / `collect-prices` — ожидаемый exit code `2` до host/DB bootstrap.
- После подключения PostgreSQL полный `dotnet test VarPrice.sln --no-build --no-restore` прошел: Worker tests 18/18, Web tests 177/177.
- `catalog-refresh` на `varprice_test` с локальным sitemap: exit code `0`, discovered/accepted/inserted = 1.
- `collect-prices` на `varprice_test` со stub extractor: exit code `0`, selected/enqueued/succeeded = 1.
- `vegetables --once` на `varprice_test` с локальным sitemap и stub extractor: exit code `0`, processed = 1, errors = 0.
- Рабочая БД `varprice` не использовалась.
