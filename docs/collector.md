# Регулярный сбор цен (collector)

MPC-89. Ежедневный сбор работает из опубликованной Release-сборки в отдельной папке, а не из `bin\Debug` репозитория. Разработка (пересборка, отладка, правки в репозитории) на сбор не влияет, пока новая версия не опубликована сознательно.

## Папки

```
J:\Apps\PriceCrawler\
  config\appsettings.Production.json   локальные настройки сборщика (пароль), не в git
  worker\<version>\                    опубликованные версии (последние 5)
  worker\current.txt                   имя версии, которую запускает scheduled-run.ps1
  logs\scheduled-run_<дата_время>.log  лог каждого запуска (хранится 30 дней)
D:\Backups\Varus\Postgres\
  varus_<yyyy-MM-dd_HHmmss>.backup     проверенные бэкапы varprice
```

Сам Worker дополнительно пишет свой лог в `worker\<version>\logs\pricecrawler-worker.log`.

## Первая настройка

1. Создать `J:\Apps\PriceCrawler\config\appsettings.Production.json` из шаблона `config\collector\appsettings.Production.example.json` и вписать пароль. База — рабочая `varprice`, `DatabaseSchema:StartupMode` обязательно `ValidateOnly`: сборщик никогда не меняет схему.
2. Опубликовать сборщик (см. ниже).
3. Один раз запустить `scheduled-run.ps1` вручную и проверить лог.

## Публикация новой версии

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts\publish-collector.ps1
```

Скрипт:
- проверяет `config\appsettings.Production.json` (нет `<placeholder>`, `ValidateOnly`);
- отказывается публиковать при незакоммиченных изменениях отслеживаемых файлов (`-AllowDirtyWorkingTree`, чтобы обойти; версия тогда получает суффикс `-dirty-<время>`);
- берёт версию из Nerdbank.GitVersioning, делает `dotnet publish -c Release` во временную папку, кладёт туда локальный `appsettings.Production.json` и `collector-release.json` (версия, коммит, время, предыдущая версия);
- проверяет запуск `PriceCrawler.Worker.exe --help`, затем переименовывает папку в `worker\<version>` и переключает `current.txt`;
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

Пока задача Планировщика не настроена (MPC-91), скрипт запускается вручную. Скрипт сам не проверяет правило «не чаще раза в сутки»; оно появится в MPC-91 (`--scheduled`).

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
