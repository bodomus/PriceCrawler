# PriceCrawler Release & Deploy Workflow

Дата: 13.08.2026  
Модель: GPT-5.5 Thinking  
Назначение: базовый порядок действий после завершения функции/тикета для выкладки Stage и Production без ручного копирования файлов.

---

## 0. Принятая схема

Корень solution:

```text
j:\Projects\c#\(!!!VARUS)\
```

Рабочие папки:

```text
j:\Projects\c#\(!!!VARUS)\
├── artifacts\
│   ├── WEB\
│   ├── crawler\
│   └── releases\
│       └── PriceCrawler-vX.Y.Z.zip
│
├── scripts\
│   ├── deploy-stage.ps1
│   ├── publish-release.ps1          # следующий скрипт, который надо добавить
│   └── smoke-test.ps1               # желательно добавить
│
└── stage\
    ├── current\
    ├── releases\
    ├── backups\
    ├── logs\
    └── shared\
        └── appsettings.Stage.json
```

Production желательно держать отдельно:

```text
j:\Services\PriceCrawler\prod\
├── current\
├── releases\
├── backups\
├── logs\
└── shared\
    └── appsettings.Production.json
```

На первом этапе можно держать `prod` рядом со `stage`, но лучше физически отделить production от исходников.

---

## 1. Отправная точка

Считаем, что:

- последняя функция/тикет закончена;
- локально всё собрано;
- тесты проходят;
- код находится в feature/codex ветке;
- нужно поднять версию;
- нужно сделать Stage;
- после проверки Stage нужно выложить Production.

---

## 2. Проверить текущее состояние Git

Из корня solution:

```powershell
cd "j:\Projects\c#\(!!!VARUS)\"
git status
git branch --show-current
```

Проверить, что ты находишься в нужной ветке, например:

```text
codex/MPC-123-some-feature
```

Посмотреть изменения:

```powershell
git diff
git diff --stat
```

Если есть новые файлы:

```powershell
git status --short
```

---

## 3. Запустить тесты локально

Минимально:

```powershell
dotnet test
```

Лучше с Release-конфигурацией:

```powershell
dotnet test -c Release
```

Если есть конкретный solution-файл:

```powershell
dotnet test .\PriceCrawler.sln -c Release
```

Если тесты используют отдельную тестовую БД, перед запуском проверить, что connection string указывает именно на test-базу, а не на рабочую/stage/prod.

---

## 4. Проверить сборку

```powershell
dotnet build .\PriceCrawler.sln -c Release
```

Если сборка и тесты зелёные, можно коммитить.

---

## 5. Закоммитить изменения

```powershell
git add .
git status --short
git commit -m "MPC-123 Implement some feature"
```

Пуш ветки:

```powershell
git push -u origin HEAD
```

---

## 6. Создать PR в GitHub

Варианты:

### Вариант A — через GitHub UI

1. Открыть репозиторий GitHub.
2. Создать Pull Request из текущей ветки в `master`.
3. Проверить, что GitHub Actions прошли.
4. Просмотреть diff.
5. Merge в `master`.

### Вариант B — через GitHub CLI

```powershell
gh pr create `
  --base master `
  --head "$(git branch --show-current)" `
  --title "MPC-123 Implement some feature" `
  --body "Implements MPC-123. Tests: dotnet test -c Release."
```

После green checks:

```powershell
gh pr merge --merge --delete-branch
```

---

## 7. Обновить локальный master

После merge:

```powershell
git checkout master
git pull origin master
```

Проверить, что локальный `master` чистый:

```powershell
git status
```

---

## 8. Поднять версию

Версию можно вести SemVer:

```text
v0.4.0  - новая функциональность
v0.4.1  - фикс
v0.5.0  - следующий функциональный блок
v1.0.0  - первая стабильная версия
```

Пример: текущая версия была `v0.3.0`, новый функциональный релиз:

```text
v0.4.0
```

Проверить существующие теги:

```powershell
git tag --sort=-version:refname
```

Создать новый тег:

```powershell
git tag v0.4.0
```

Пуш тега:

```powershell
git push origin v0.4.0
```

Важно: тег должен ставиться только на `master`, после merge PR.

---

## 9. Собрать локальный релизный ZIP

На текущем этапе, пока GitHub Release ещё не автоматизирован, можно собрать ZIP локально.

Ожидаемый результат:

```text
artifacts\releases\PriceCrawler-v0.4.0.zip
```

Внутри ZIP:

```text
WEB\
crawler\
version.json
release-notes.md
```

Если `publish-release.ps1` ещё не создан, временная ручная команда:

```powershell
$Version = "v0.4.0"
$Root = "j:\Projects\c#\(!!!VARUS)"
$ReleaseDir = Join-Path $Root "artifacts\releases"
$Zip = Join-Path $ReleaseDir "PriceCrawler-$Version.zip"

New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null

if (Test-Path $Zip) {
    Remove-Item $Zip -Force
}

Compress-Archive `
  -Path `
    (Join-Path $Root "artifacts\WEB"), `
    (Join-Path $Root "artifacts\crawler") `
  -DestinationPath $Zip `
  -Force
```

Проверить ZIP:

```powershell
Get-Item ".\artifacts\releases\PriceCrawler-v0.4.0.zip"
```

---

## 10. Выложить Stage

Команда из корня solution:

```powershell
cd "j:\Projects\c#\(!!!VARUS)\"

.\scripts\deploy-stage.ps1 `
  -Version v0.4.0 `
  -ZipPath ".\artifacts\releases\PriceCrawler-v0.4.0.zip"
```

Скрипт должен выполнить:

```text
1. Взять локальный ZIP.
2. Распаковать в stage\releases\v0.4.0.
3. Остановить Stage WEB и Stage crawler.
4. Сделать backup stage\current.
5. Очистить stage\current.
6. Скопировать релиз в stage\current.
7. Подложить stage\shared\appsettings.Stage.json.
8. Запустить Stage WEB и Stage crawler.
9. Записать stage\logs\deployments.log.
```

---

## 11. Проверить Stage

Минимальные проверки:

```powershell
git status
Get-Content ".\stage\logs\deployments.log" -Tail 20
```

Проверить процессы:

```powershell
Get-Process | Where-Object {
    $_.ProcessName -like "*PriceCrawler*"
}
```

Если есть health endpoint:

```powershell
Invoke-WebRequest "http://localhost:5101/health"
Invoke-WebRequest "http://localhost:5101/version"
```

Проверить UI в браузере:

```text
http://localhost:5101
```

Проверить, что Stage подключен именно к stage-базе, а не к production.

Обязательно проверить:

```text
- приложение стартует;
- worker стартует;
- логи пишутся;
- connection string Stage правильный;
- тестовый сбор/парсинг проходит;
- в UI видна новая версия;
- нет ошибок миграций;
- нет ошибок доступа к файлам;
- нет случайного подключения к рабочей/production базе.
```

---

## 12. Если Stage плохой

Сначала остановить процессы:

```powershell
Get-Process -Name "PriceCrawler.Web" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name "PriceCrawler.Crawler" -ErrorAction SilentlyContinue | Stop-Process -Force
```

Откатить `stage\current` из последнего backup:

```powershell
# пример вручную
Remove-Item ".\stage\current\*" -Recurse -Force
Copy-Item ".\stage\backups\current-YYYYMMDD-HHMMSS\*" ".\stage\current" -Recurse -Force
```

Затем поднять старую версию.

В будущем лучше сделать отдельный:

```powershell
.\scripts\rollback-stage.ps1 -Version v0.3.0
```

---

## 13. Production deploy

Production выкладывается только после Stage.

Желательный порядок:

```text
1. Stage развернут.
2. Smoke test пройден.
3. Проверено подключение к stage DB.
4. Версия подтверждена.
5. Только потом Production.
```

Команда в будущем:

```powershell
.\scripts\deploy-production.ps1 `
  -Version v0.4.0 `
  -ZipPath ".\artifacts\releases\PriceCrawler-v0.4.0.zip"
```

Или лучше единый скрипт:

```powershell
.\scripts\deploy.ps1 `
  -Environment Production `
  -Version v0.4.0 `
  -ZipPath ".\artifacts\releases\PriceCrawler-v0.4.0.zip"
```

Production-скрипт должен делать то же, что Stage, но дополнительно:

```text
1. Проверить, что используется Production config.
2. Сделать backup production DB.
3. Остановить production services.
4. Развернуть тот же ZIP, который был проверен на Stage.
5. Подложить appsettings.Production.json.
6. Запустить production services.
7. Проверить /health и /version.
8. Записать production deploy log.
```

---

## 14. GitHub Release

После локального успешного Stage можно создать GitHub Release.

Если тег уже отправлен:

```powershell
git tag --sort=-version:refname
```

Создать release через GitHub CLI:

```powershell
gh release create v0.4.0 `
  ".\artifacts\releases\PriceCrawler-v0.4.0.zip" `
  --title "PriceCrawler v0.4.0" `
  --notes "MPC-123: Implemented feature. Tests passed. Stage deployed successfully."
```

Если релиз уже существует и надо добавить ZIP:

```powershell
gh release upload v0.4.0 ".\artifacts\releases\PriceCrawler-v0.4.0.zip" --clobber
```

---

## 15. Что должно быть в release notes

Пример:

```markdown
# PriceCrawler v0.4.0

## Tickets
- MPC-123 Implemented some feature

## Build
- Configuration: Release
- Tests: Passed
- Commit: <commit-sha>

## Database
- Migrations: yes/no
- Backup required: yes/no

## Deployment
- Stage: deployed and checked
- Production: pending / deployed
```

---

## 16. Рекомендуемая команда полного цикла

Когда всё будет автоматизировано, идеальный цикл должен выглядеть так:

```powershell
cd "j:\Projects\c#\(!!!VARUS)\"

dotnet test .\PriceCrawler.sln -c Release

git checkout master
git pull origin master

git tag v0.4.0
git push origin v0.4.0

.\scripts\publish-release.ps1 -Version v0.4.0

.\scripts\deploy-stage.ps1 `
  -Version v0.4.0 `
  -ZipPath ".\artifacts\releases\PriceCrawler-v0.4.0.zip"

.\scripts\smoke-test.ps1 -Environment Stage

.\scripts\deploy-production.ps1 `
  -Version v0.4.0 `
  -ZipPath ".\artifacts\releases\PriceCrawler-v0.4.0.zip"

.\scripts\smoke-test.ps1 -Environment Production
```

---

## 17. Что надо сделать следующим тикетом

Следующий практический тикет для Codex:

```text
MPC-DEPLOY-2: Add local release packaging script
```

Содержание:

```text
1. Add scripts/publish-release.ps1.
2. Script accepts -Version vX.Y.Z.
3. Script resolves solution root relatively from scripts folder.
4. Script validates artifacts\WEB and artifacts\crawler exist.
5. Script creates artifacts\releases\PriceCrawler-vX.Y.Z.zip.
6. Script writes version.json into ZIP:
   - version
   - commit
   - branch
   - buildDateUtc
7. Script writes release-notes.md placeholder into ZIP.
8. Script refuses to overwrite existing ZIP unless -Force is passed.
9. Add docs\RELEASE_AND_DEPLOY.md with the workflow.
```

После этого:

```text
MPC-DEPLOY-3: Add GitHub Actions release ZIP on git tag
MPC-DEPLOY-4: Add unified deploy.ps1 for Stage and Production
MPC-DEPLOY-5: Add smoke-test.ps1 and /version endpoint
MPC-DEPLOY-6: Add DB backup and migration step before deploy
```

---

## 18. Главное правило

Не копировать фиксы руками в Stage/Production.

Правильная цепочка:

```text
feature branch
    ↓
PR
    ↓
tests
    ↓
merge to master
    ↓
git tag vX.Y.Z
    ↓
release ZIP
    ↓
deploy Stage
    ↓
smoke test
    ↓
deploy Production
```

Production должен получать тот же ZIP, который уже был проверен на Stage.
