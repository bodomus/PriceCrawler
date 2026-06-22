# MPC-65 — Резюме выполнения

## Summary

Реализован явный CLI-контракт для `VarPrice.Worker`: основные режимы теперь запускаются позиционными командами `vegetables`, `catalog-refresh`, `collect-prices`, а `--help` / `-h` выводят справку без создания host и без DB bootstrap.

## Сделано

- Добавлен `WorkerCommandParser`, который централизованно разбирает команды Worker.
- Добавлены типы `WorkerRunMode` и `WorkerCommand` для явного выбора режима.
- `Program.cs` переведен с ручного поиска аргументов на результат parser-а.
- Некорректные команды, неизвестные опции, отсутствующее значение `--job` и конфликтующие режимы завершаются с exit code `2`.
- Сохранена обратная совместимость для legacy aliases: `--job <name>`, `--collect-prices`, запуск без аргументов как alias для `vegetables`.
- Документация в `README.md` обновлена под явные команды.
- `CHANGELOG.md` обновлен записью о новом CLI-контракте Worker.
- Добавлены unit-тесты `WorkerCommandParserTests`.

## Validation

- `dotnet build VarPrice.sln`
- `dotnet test VarPrice.Web.Tests\VarPrice.Web.Tests.csproj --no-build --filter WorkerCommandParserTests`
- `dotnet run --no-build --project VarPrice.Worker -- --help`
- `dotnet run --no-build --project VarPrice.Worker -- unknown` с проверкой exit code `2`
- `dotnet test VarPrice.sln --no-build`

Полный тестовый прогон прошел на тестовой БД `varprice_test` через существующий `PostgresIntegrationFixture`.

## Notes

- Изменений схемы БД не было.
- Рабочая БД `varprice` не использовалась для тестов.
- Доступный YouTrack MCP позволяет обновлять поля задачи, но не предоставляет отдельного метода прикрепления файла; локальный markdown тикета сохранен в `Tickets/MPC-65.md`.
