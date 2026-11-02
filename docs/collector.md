# Регулярный сбор цен (collector)

MPC-89. Ежедневный сбор работает из опубликованной Release-сборки в отдельной папке, а не из `bin\Debug` репозитория. Разработка (пересборка, отладка, правки в репозитории) на сбор не влияет, пока новая версия не опубликована сознательно.

## Папки

```
J:\Apps\PriceCrawler\
  config\appsettings.Production.json   локальные настройки сборщика (пароль), не в git
  worker\<version>\                    опубликованные версии (последние 5)
  worker\current.txt                   имя версии, которую запускает scheduled-run.ps1
  scripts\                             копии scheduled-run.ps1 и backup-database.ps1 для Планировщика
  state\last-scheduled-success.txt     дата последнего успешного запуска из Планировщика
  logs\scheduled-run_<дата_время>.log  лог каждого запуска (хранится 30 дней)
  logs\scheduled-run_skipped.log       по строке на каждый пропущенный повтор («уже собрано сегодня»)
D:\Backups\Varus\Postgres\
  varus_<yyyy-MM-dd_HHmmss>.backup     проверенные бэкапы varprice
```

Сам Worker дополнительно пишет свой лог в `worker\<version>\logs\pricecrawler-worker.log`.

## Первая настройка

1. Создать `J:\Apps\PriceCrawler\config\appsettings.Production.json` из шаблона `config\collector\appsettings.Production.example.json` и вписать пароль. База — рабочая `varprice`, `DatabaseSchema:StartupMode` обязательно `ValidateOnly`: сборщик никогда не меняет схему.
2. Опубликовать сборщик (см. ниже).
3. Один раз запустить `scheduled-run.ps1` вручную и проверить лог.
4. Зарегистрировать задачу Планировщика (см. «Планировщик Windows»).

## Публикация новой версии

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts\publish-collector.ps1
```

Скрипт:
- проверяет `config\appsettings.Production.json` (нет `<placeholder>`, `ValidateOnly`);
- отказывается публиковать при незакоммиченных изменениях отслеживаемых файлов (`-AllowDirtyWorkingTree`, чтобы обойти; версия тогда получает суффикс `-dirty-<время>`);
- берёт версию из Nerdbank.GitVersioning, делает `dotnet publish -c Release` во временную папку, кладёт туда локальный `appsettings.Production.json` и `collector-release.json` (версия, коммит, время, предыдущая версия);
- проверяет запуск `PriceCrawler.Worker.exe --help`, затем переименовывает папку в `worker\<version>` и переключает `current.txt`;
- копирует `scheduled-run.ps1` и `backup-database.ps1` в `J:\Apps\PriceCrawler\scripts\`: Планировщик запускает эти копии, поэтому смена ветки или правки скриптов в репозитории не влияют на ежедневный сбор до следующей публикации;
- хранит последние 5 версий (`-KeepVersions`), текущую и предыдущую не удаляет никогда.

Повторная публикация той же версии требует `-Force`.

**Откат:** записать в `worker\current.txt` имя предыдущей версии (оно есть в `collector-release.json` текущей).

## Ежедневный запуск

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts\scheduled-run.ps1
```

Шаги:
1. Docker Engine: если `docker info` не отвечает, запускает Docker Desktop и ждёт до 5 мин.
2. PostgreSQL: `docker start var_postgres`, ждёт `pg_isready`. Контейнер должен существовать (создаётся один раз `docker compose up -d postgres` из репозитория).
3. Worker из `worker\current.txt` с `DOTNET_ENVIRONMENT=Production`:
   - по воскресеньям `catalog-refresh`, затем `collect-prices`;
   - в остальные дни `collect-prices`.
   Режим можно задать явно: `-Mode collect-prices` или `-Mode catalog-refresh`.
4. Бэкап `varprice` (`Scripts\backup-database.ps1`) — **после** Worker и даже если Worker упал. Пропускается только если база недоступна или передан `-SkipBackup`.
5. Удаление логов старше 30 дней (`-LogRetentionDays`).

Одновременно может идти только один запуск (второй сразу выходит с кодом 40).

Ручной запуск (без `-Scheduled`) ничем не ограничен: его можно повторять сколько угодно раз в день.

### Режим `-Scheduled` (MPC-91)

С ключом `-Scheduled` (так его запускает Планировщик) действует правило «не чаще одного успешного сбора в сутки»:
- если в `state\last-scheduled-success.txt` уже сегодняшняя дата, скрипт сразу выходит с кодом 0, **не запуская Docker**, и дописывает строку в `logs\scheduled-run_skipped.log`;
- дата записывается, только когда все шаги Worker завершились с кодом 0. Неудачный бэкап (код 30) сбор не повторяет;
- если запуск прервался (отключили свет, перезагрузка) или Worker упал, дата не записывается, и следующий повтор Планировщика запускает сбор снова. `collect-prices` берёт только те позиции каталога, которые ещё не собраны, поэтому повтор после обрыва дособирает остаток, а не начинает всё заново.

Правило реализовано в `scheduled-run.ps1`, а не флагом `--scheduled` в Worker (как было сформулировано в MPC-91): так ежечасный повтор не будит Docker и PostgreSQL, а Worker не нужно перепубликовать.

## Планировщик Windows

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts\register-scheduled-task.ps1
```

Запускается один раз (и повторно после изменения параметров) от пользователя, под которым работает компьютер; права администратора не нужны. Перед этим должен быть выполнен `publish-collector.ps1`, иначе скрипт остановится: в `J:\Apps\PriceCrawler\scripts\` ещё нет `scheduled-run.ps1`.

Задача `PriceCrawler Daily Collect`:
- старт ежедневно в **08:00** (`-StartTime`), затем повтор **каждый час до 22:00** (`-RetryHours 14`). Из-за частых отключений света: первый повтор после включения компьютера доделает сбор, остальные повторы этого дня сразу выходят;
- «Запускать при первой возможности, если пропущен плановый запуск» (`StartWhenAvailable`): если в 08:00 компьютер был выключен, задача стартует после включения;
- если предыдущий запуск ещё идёт (сбор ~3,5 ч), новый экземпляр не стартует (`MultipleInstances IgnoreNew`); на всякий случай есть ещё мьютекс в скрипте (код 40);
- лимит времени выполнения 8 ч (`-TimeLimitHours`);
- работает только когда пользователь вошёл в систему: Docker Desktop запускается в сессии пользователя. После включения компьютера нужно войти в Windows (или настроить автоматический вход);
- действие: `powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File J:\Apps\PriceCrawler\scripts\scheduled-run.ps1 -Scheduled -AppRoot J:\Apps\PriceCrawler`.

Скрипт удаляет старую сломанную задачу `\Magazine Crawler` (`crawler.bat` → несуществующий `VarPrice.Worker.exe`); оставить её: `-KeepLegacyTask`. Если старую задачу создал администратор, без повышенных прав её не удалить: скрипт выводит предупреждение, а новая задача при этом уже зарегистрирована. Удалить вручную из PowerShell, запущенного от имени администратора: `Unregister-ScheduledTask -TaskName "Magazine Crawler" -Confirm:$false`. Посмотреть, что будет сделано, без изменений: `-WhatIf`.

Проверка: `Get-ScheduledTask "PriceCrawler Daily Collect" | Get-ScheduledTaskInfo` (следующий и последний запуск, `LastTaskResult` = код выхода `scheduled-run.ps1`) или «Планировщик заданий» → «Библиотека». Запустить сейчас: `Start-ScheduledTask "PriceCrawler Daily Collect"` (если сегодня уже был успешный запуск, он будет пропущен).

Отключить: `Disable-ScheduledTask "PriceCrawler Daily Collect"`; удалить: `Unregister-ScheduledTask "PriceCrawler Daily Collect"`.

### Коды выхода `scheduled-run.ps1`

| Код | Этап |
|---|---|
| 0 | всё успешно |
| 1 | непредвиденная ошибка скрипта |
| 10 | Docker Engine не поднялся |
| 11 | контейнер PostgreSQL не найден или не готов |
| 20 | опубликованный Worker не найден (нет `current.txt` или exe) |
| 21 | `catalog-refresh` завершился с ошибкой |
| 22 | `collect-prices` завершился с ошибкой |
| 30 | бэкап не удался |
| 40 | уже идёт другой запуск |

Если ошибок несколько, возвращается первая; все они есть в логе.

## Бэкап

`Scripts\backup-database.ps1` (его же вызывает ручной `Scripts\backup_varus.bat`):
- контейнер по имени `var_postgres`, а не по id: пересоздание контейнера бэкап не ломает;
- `pg_dump -F c` внутри контейнера, проверка `pg_restore --list`, копирование в `D:\Backups\Varus\Postgres\varus_<yyyy-MM-dd_HHmmss>.backup`, проверка, что файл не пустой;
- в имени есть время, поэтому повторный запуск в тот же день не перезаписывает утренний файл;
- ротация: все бэкапы за последние 14 дней плюс самый свежий бэкап каждой недели за 13 недель (≈3 месяца). Самый свежий бэкап не удаляется никогда.
- **Старые файлы `varus_yyyy-MM-dd.backup`** (от прежнего `backup_varus.bat`) ротация не трогает, их можно удалить вручную.

Посмотреть, что удалит ротация, не удаляя: `backup-database.ps1 -RotateOnly -WhatIf`.

Восстановление: `Scripts\restore_varus.bat 2026-10-29_061500` (для старых файлов — `restore_varus.bat 2026-03-15`). Это **перезаписывает** `varprice`, только осознанно.

Бэкапы лежат на том же компьютере. Копия в облако пока не настроена.
