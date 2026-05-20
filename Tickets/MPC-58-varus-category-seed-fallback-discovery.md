# MPC-58: Varus - добавить fallback discovery товаров через seed categories при недоступном sitemap

## Goal

Добавить контролируемый fallback для discovery product URLs в Varus crawler.

Основной путь discovery остается sitemap. Если sitemap discovery завершился неуспешно (`SitemapUnavailable` / `sitemap discovery failed`), crawler должен попробовать получить product URLs из заранее заданных seed category URLs, затем передать найденные URL в существующий crawler pipeline.

Fallback нужен только для ограниченного набора seed categories. Не требуется и не допускается полный recursive crawler всего сайта.

---

## Hard Constraints

- Не делать stage-разделение.
- Не создавать `appsettings.Stage.json`, если его нет.
- Не менять DB-схему.
- Не менять UI.
- Не делать полный recursive crawler всего сайта.
- Работать только по seed category URLs.
- Основной путь через sitemap должен остаться основным.
- Существующий crawler pipeline должен переиспользоваться, а не дублироваться.

---

## Problem

После MPC-57 crawler должен корректно классифицировать недоступный или невалидный sitemap. Но если sitemap временно недоступен, сбор цен полностью блокируется, хотя product URLs можно получить из HTML seed category pages.

Нужен ограниченный fallback:

1. Сначала пробуем sitemap discovery.
2. Если sitemap успешен и вернул product URLs - используем sitemap URLs.
3. Если sitemap недоступен / discovery failed - читаем seed category URLs.
4. Загружаем страницы seed categories.
5. Извлекаем product URLs из HTML категории.
6. Применяем существующий `UrlFilter`.
7. Передаем URLs в существующий crawler pipeline.
8. Если категории тоже не дали URLs - завершаем run контролируемой ошибкой `ProductUrlDiscoveryUnavailable`.

---

## Required Design

Заложить общий контракт discovery source:

```csharp
public interface IProductUrlDiscoverySource
{
    Task<IReadOnlyCollection<Uri>> DiscoverProductUrlsAsync(CancellationToken ct);
}
```

Реализации:

```text
SitemapProductUrlDiscoverySource
CategoryProductUrlDiscoverySource
```

Общий сервис:

```text
ProductUrlDiscoveryService
```

Ответственность `ProductUrlDiscoveryService`:

```text
try sitemap
if sitemap ok and urls > 0 -> use sitemap urls
else try category seed urls
if category urls > 0 -> use category urls
else fail controlled
```

---

## Configuration

Добавить или использовать существующий config key:

```text
CategorySeedUrlsFilePath
```

Файл должен содержать seed category URLs, например по одному URL на строку.

Требования:

- пустые строки игнорировать;
- строки с пробелами обрезать через `Trim`;
- malformed URLs логировать как warning и пропускать;
- если файл не задан или не найден, fallback должен завершиться контролируемо и диагностируемо;
- не добавлять отдельный stage config.

---

## Category Discovery Rules

`CategoryProductUrlDiscoverySource` должен:

1. Прочитать `CategorySeedUrlsFilePath`.
2. Загрузить только указанные seed category URLs.
3. Для каждой seed category пройти страницы пагинации категории, если пагинация явно обнаружена.
4. Не уходить в recursive обход child categories и других разделов.
5. Извлечь product URLs из HTML.
6. Нормализовать относительные ссылки в абсолютные.
7. Убрать дубликаты.
8. Применить существующий `UrlFilter`.
9. Вернуть итоговый набор product URLs.

Для Varus product links на category page ожидаемый источник:

```text
<a href="/product-slug-or-url-path">
```

При извлечении ссылок учитывать, что URL товара обычно выглядит как:

```text
https://varus.ua/<product-url-path>
```

а city prefix в category URL (`/dnipro/...`) может отсутствовать в product URL.

---

## Pagination

Fallback должен идти только по страницам seed category.

Допустимые варианты:

- использовать ссылки пагинации, если они есть в HTML;
- использовать существующий pattern `?page=N`, если он уже применим в проекте;
- остановиться, когда очередная страница не дает новых product URLs;
- иметь разумный верхний предел страниц на seed category из config или константы, чтобы исключить бесконечный обход.

Не нужно:

- обходить все ссылки категории;
- обходить меню;
- строить карту сайта;
- заходить в другие категории, бренды, акции, поиск.

---

## Failure Classification

Добавить контролируемую ошибку:

```text
ProductUrlDiscoveryUnavailable
```

Она должна использоваться, когда:

- sitemap discovery недоступен или не дал URL;
- category fallback не настроен, недоступен или не дал URL;
- в итоге не найдено ни одного product URL.

Ошибка должна быть отличима от сетевых и parsing ошибок отдельных category pages.

---

## Logging Requirements

При успешном sitemap path:

```text
Product URL discovery completed using sitemap.
Count=N
```

При переходе к fallback:

```text
Sitemap product URL discovery unavailable. Trying category seed fallback.
Reason=...
```

При чтении seed file:

```text
Category seed URLs loaded.
FilePath=...
Count=N
```

При обработке category page:

```text
Category seed page processed.
Url=...
Page=N
ProductUrlsFound=N
NewProductUrls=N
```

Если fallback дал URLs:

```text
Product URL discovery completed using category seed fallback.
SeedCategoryCount=N
ProductUrlCount=N
```

Если не найдено ничего:

```text
Product URL discovery failed. No product URLs available from sitemap or category seed fallback.
FailureKind=ProductUrlDiscoveryUnavailable
```

---

## Acceptance Criteria

1. Если sitemap discovery успешен и вернул URLs, category fallback не запускается.
2. Если sitemap discovery завершился `SitemapUnavailable` / discovery failed, запускается category fallback.
3. Category fallback читает `CategorySeedUrlsFilePath`.
4. Category fallback загружает только seed category URLs и их пагинацию.
5. Product URLs извлекаются из category HTML.
6. Product URLs проходят через существующий `UrlFilter`.
7. Итоговые URLs передаются в существующий crawler pipeline.
8. Если sitemap и category fallback не дали URLs, run завершается контролируемой ошибкой `ProductUrlDiscoveryUnavailable`.
9. Не создается `appsettings.Stage.json`.
10. Не меняется DB schema.
11. Не меняется UI.
12. Не запускается полный recursive crawler сайта.

---

## Suggested Tests

Добавить focused tests, если текущая структура проекта позволяет:

- sitemap source returns URLs -> service returns sitemap URLs and does not call category source;
- sitemap source throws/returns unavailable -> service calls category source;
- category source returns URLs -> service returns filtered category URLs;
- both sources unavailable/empty -> service fails with `ProductUrlDiscoveryUnavailable`;
- seed file parser ignores blank lines and invalid URLs;
- category HTML parser extracts product links and normalizes relative URLs;
- category fallback does not follow non-product/category/menu links.

---

## Out Of Scope

- Stage environment split.
- `appsettings.Stage.json`.
- Database migrations or schema changes.
- UI changes.
- Full-site recursive crawling.
- Automatic discovery of all categories from menu or sitemap.
- New crawler pipeline.
