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
3. Получит версию из Git tag текущего commit.
4. Выполнит `dotnet restore`.
5. Выполнит `dotnet test`.
6. Очистит старые publish-каталоги.
7. Опубликует `PriceCrawler.Web`.
8. Опубликует `PriceCrawler.Worker`.
9. Проверит, что publish-каталоги не пустые.
10. Проверит наличие исполняемых файлов или DLL.
11. Создаст release-пакет.
12. Добавит в пакет файл `release.json`.

---

## 5. Проверить результат

После успешного выполнения должен появиться файл:

```text
artifacts\release\PriceCrawler-v0.4.1.zip
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
release.json
```

Проверить ZIP:

```powershell
Get-Item ".\artifacts\release\PriceCrawler-v0.4.1.zip"
```

При необходимости посмотреть его содержимое:

```powershell
tar -tf ".\artifacts\release\PriceCrawler-v0.4.1.zip"
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
artifacts\release\PriceCrawler-v0.4.1.zip
```

---

## Правила релиза

- Один Git tag соответствует одному release-пакету.
- ZIP должен собираться только из чистого рабочего дерева.
- Перед publish должны успешно пройти тесты.
- Конфигурации Stage и Production с секретами не должны входить в ZIP.
- Connection strings, пароли и API keys должны подкладываться при deploy или передаваться через переменные окружения.
- Не следует вручную изменять содержимое ZIP после выполнения `build-release.ps1`.
- При исправлении уже выпущенной версии нужно создавать новый patch-релиз, например `v0.4.2`, а не пересобирать `v0.4.1`.
