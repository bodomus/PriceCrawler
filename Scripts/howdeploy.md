# PriceCrawler — порядок подготовки релиза

Этот документ описывает стандартную последовательность создания release-пакета PriceCrawler.

## 1. Проверить состояние репозитория

Перейти в корень solution:

```powershell
cd "J:\Projects\c#\(!!!VARUS)"
```

Проверить текущую ветку и незакоммиченные изменения:

```powershell
git branch --show-current
git status
```

Перед созданием релиза рабочая директория должна быть чистой.

---

## 2. Зафиксировать изменения

Добавить изменения в Git:

```powershell
git add .
```

Создать commit:

```powershell
git commit -m "Prepare release v0.4.1"
```

Отправить commit в удалённый репозиторий:

```powershell
git push
```

Номер версии в сообщении commit должен соответствовать создаваемому релизу.

---

## 3. Создать Git tag

Создать тег на текущем commit:

```powershell
git tag v0.4.1
```

Отправить тег в GitHub:

```powershell
git push origin v0.4.1
```

Рекомендуемый формат тегов:

```text
vMAJOR.MINOR.PATCH
```

Примеры:

```text
v0.4.1
v0.5.0
v1.0.0
```

После создания тега можно проверить, что текущий commit действительно помечен этим тегом:

```powershell
git describe --tags --exact-match HEAD
```

Ожидаемый результат:

```text
v0.4.1
```

---

## 4. Запустить сборку релиза

Из корня solution выполнить:

```powershell
.\scripts\build-release.ps1
```

Скрипт автоматически:

1. Проверит структуру репозитория.
2. Проверит состояние Git working tree.
3. Получит application version и commit из Nerdbank.GitVersioning/Git (на release tag это версия тега).
4. Выполнит `dotnet restore`.
5. Выполнит `dotnet test`.
6. Очистит старые publish-каталоги.
7. Опубликует `PriceCrawler.Web`.
8. Опубликует `PriceCrawler.Worker`.
9. Проверит, что publish-каталоги не пустые.
10. Проверит наличие исполняемых файлов или DLL.
11. Соберёт минимальный staging tree с безопасными placeholder-конфигурациями.
12. Добавит numbered migrations, bootstrap support и runtime-role provisioning script.
13. Создаст и полностью проверит `release.json`.
14. Проверит forbidden paths, plaintext secrets и Stage/Production `ValidateOnly` до и после ZIP.
15. Создаст ZIP с детерминированным порядком entries.
16. Вычислит SHA-256 и создаст sidecar `.zip.sha256`.

---

## 5. Проверить результат

После успешного выполнения должен появиться файл:

```text
artifacts\releases\PriceCrawler-v0.4.1.zip
artifacts\releases\PriceCrawler-v0.4.1.zip.sha256
```

Промежуточные publish-файлы находятся здесь:

```text
artifacts\publish\web
artifacts\publish\crawler
```

Внутренняя структура ZIP:

```text
web/
crawler/
db/migrations/
db/scripts/
db/README.md
release.json
```

`release.json` содержит `product`, `version`, exact `commit`, `builtAtUtc`, component presence, ordered migration inventory и диапазон совместимости схемы. Для текущей схемы диапазон равен `1 -> 1`.

Web/Crawler subtree не содержит копий `schema.sql`, legacy DB routines, Development/Test appsettings или локального connection string. Stage и Production configuration templates остаются `ValidateOnly`; реальные credentials поступают только при deployment.

Перед запуском Web или Worker deployment обязан применить требуемые forward migrations и проверить target schema version. Stage-конфигурация должна содержать:

```json
{
  "DatabaseSchema": {
    "StartupMode": "ValidateOnly"
  }
}
```

Приложение повторно проверяет `schema_version` при старте, но не выполняет baseline, bootstrap, migrations или repair. Unsafe override `DatabaseSchema__StartupMode=Ensure` завершает Stage/Production startup до обращения к базе.

> Stage and Production schema changes belong to deployment, not application startup.

Первичное создание баз не является частью обычного release deploy. Перед первым Stage/Production deployment используйте `scripts/initialize-database-environments.ps1` по инструкции `docs/database-provisioning.md`. Production bootstrap допускается ровно один раз; после него Development dump больше никогда не применяется к Production.

> After initial bootstrap, Production must never be replaced from Development.

Перед запуском Stage/Production Web и Worker создайте отдельные runtime-роли командой `scripts/provision-database-runtime-roles.ps1` по процедуре из `docs/database-provisioning.md`. Credentials должны быть внедрены secret store в `ConnectionStrings__Postgres` отдельно для каждого процесса:

```text
Stage Web        -> pricecrawler_stage_web
Stage Worker     -> pricecrawler_stage_worker
Production Web   -> pricecrawler_prod_web
Production Worker-> pricecrawler_prod_worker
```

Runtime connection string не должен использовать deploy/admin identity. После каждой forward migration повторно примените runtime grants; Web/Worker по-прежнему запускаются только с `DatabaseSchema__StartupMode=ValidateOnly`.

Проверить ZIP:

```powershell
Get-Item ".\artifacts\releases\PriceCrawler-v0.4.1.zip"
```

При необходимости посмотреть его содержимое:

```powershell
tar -tf ".\artifacts\releases\PriceCrawler-v0.4.1.zip"
Get-Content ".\artifacts\releases\PriceCrawler-v0.4.1.zip.sha256"
Get-FileHash ".\artifacts\releases\PriceCrawler-v0.4.1.zip" -Algorithm SHA256
```

---

## 6. Тестовая сборка без Git tag

Для локальной проверки версию можно передать вручную:

```powershell
.\scripts\build-release.ps1 -Version v0.4.1
```

Этот режим допустим для локальной проверки, но не рекомендуется для официального release.

---

## 7. Дополнительные параметры

Выбрать каталог результата (relative path считается от repository root):

```powershell
.\scripts\build-release.ps1 -OutputDirectory artifacts\releases-candidate
```

Скрипт никогда молча не перезаписывает ZIP или checksum. Только для явно одобренной локальной пересборки:

```powershell
.\scripts\build-release.ps1 `
    -Version v0.4.1-local `
    -ReplaceExistingArtifact `
    -AllowDirtyWorkingTree
```

Пропустить тесты:

```powershell
.\scripts\build-release.ps1 -Version v0.4.1 -SkipTests
```

Разрешить сборку при незакоммиченных изменениях:

```powershell
.\scripts\build-release.ps1 `
    -Version v0.4.1-dev `
    -AllowDirtyWorkingTree
```

Для официального релиза параметры `-SkipTests` и `-AllowDirtyWorkingTree` использовать не следует.

---

## Полная последовательность команд

```powershell
cd "J:\Projects\c#\(!!!VARUS)"

git status
git add .
git commit -m "Prepare release v0.4.1"
git push

git tag v0.4.1
git push origin v0.4.1

git describe --tags --exact-match HEAD

.\scripts\build-release.ps1
```

Результат:

```text
artifacts\releases\PriceCrawler-v0.4.1.zip
artifacts\releases\PriceCrawler-v0.4.1.zip.sha256
```

Stage deployment after package creation is performed only by `Scripts/deploy-stage.ps1`. Use the normal, explicit Development-refresh, or non-mutating `-WhatIf` commands from `docs/stage-deployment.md`. The deploy verifies the sidecar/package, creates and verifies a Stage backup, applies forward-only migrations and Stage-only runtime grants, activates `current`, verifies Web port and `/health`, and only then starts Worker. Production and schema downgrade are unsupported.

Production deployment is performed only by `Scripts/deploy-production.ps1` and requires the successful JSON report from that Stage deployment for the exact ZIP. Run `-WhatIf` first using `docs/production-deployment.md`; a real deploy additionally requires `-ConfirmProductionDeployment`. The script validates the Production independence marker, creates a verified backup before mutation, applies only forward migrations and Production-only runtime grants, and starts Worker only after Web listener and health verification. It never copies another database into Production.

---

## Правила релиза

- Один Git tag соответствует одному release-пакету.
- ZIP должен собираться только из чистого рабочего дерева.
- Перед publish должны успешно пройти тесты.
- Конфигурации Stage и Production с секретами не должны входить в ZIP.
- `*.dump`, backups, logs, `.env`, `.pgpass`, Graphify/CRG data и test results запрещены в ZIP.
- Connection strings, пароли и API keys должны подкладываться при deploy или передаваться через переменные окружения.
- `release.json` не содержит machine-specific absolute paths и не разрешает application startup migrations.
- Гарантия детерминизма: normalized paths, ordinal entry ordering и единый UTC timestamp для ZIP entries; byte-for-byte equality не гарантируется, потому что `builtAtUtc` меняется между сборками.
- Не следует вручную изменять содержимое ZIP после выполнения `build-release.ps1`.
- При исправлении уже выпущенной версии нужно создавать новый patch-релиз, например `v0.4.2`, а не пересобирать `v0.4.1`.
