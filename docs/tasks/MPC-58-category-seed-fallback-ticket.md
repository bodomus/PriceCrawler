# MPC-58: Varus — добавить fallback discovery товаров через seed categories при недоступном sitemap

## Goal

Добавить резервный механизм обнаружения product URLs для PriceCrawler.Worker.

Основной путь остаётся прежним:

```text
sitemap discovery -> sitemap parsing -> product URL filtering -> existing crawler pipeline
```

Новый fallback-путь должен включаться только если sitemap discovery завершился контролируемой ошибкой `SitemapUnavailable` или не дал валидных product URLs.

Fallback должен брать список стартовых категорий из файла:

```text
config/category-seed-urls.varus.json
```

и пытаться собрать ссылки на товары со страниц этих категорий.

---

## Context

В MPC-57 был добавлен устойчивый sitemap discovery/validation.

Теперь crawler умеет корректно обрабатывать ситуацию, когда Varus публикует битую sitemap-ссылку в `robots.txt`.

Пример реальной проблемы:

```text
robots.txt:
Sitemap: https://varus.ua/sitemap-index.xml
```

но сам URL может возвращать:

```http
HTTP/1.1 404 Not Found
Content-Type: text/html; charset=UTF-8
```

и HTML-страницу Magento `404 Not Found` вместо XML sitemap.

MPC-57 защищает Worker от падения, но не даёт альтернативный источник product URLs.

MPC-58 должен добавить такой альтернативный источник.

---

## Required Configuration

Добавить поддержку настройки:

```json
{
  "Crawler": {
    "CategorySeedUrlsFilePath": "config/category-seed-urls.varus.json"
  }
}
```

Файл `category-seed-urls.varus.json` должен иметь формат:

```json
{
  "Crawler": {
    "CategorySeedUrls": [
      {
        "name": "Органічні продукти",
        "url": "https://varus.ua/organic-food"
      },
      {
        "name": "Веганські продукти",
        "url": "https://varus.ua/vegan-food"
      }
    ]
  }
}
```

Требования к файлу:

```text
- CategorySeedUrls должен быть массивом объектов.
- Каждый объект должен иметь поля name и url.
- name не должен быть пустым.
- url должен быть абсолютным https URL.
- Дубликаты по url должны удаляться.
- Невалидные записи должны логироваться как warning.
```

---

## Important Branch Constraint

В текущей ветке может не быть разделения на Dev/Stage и может не быть `appsettings.Stage.json`.

Не создавать искусственно `appsettings.Stage.json`, если его нет в ветке.

Не реализовывать Stage-разделение в рамках этого тикета.

Добавить `Crawler:CategorySeedUrlsFilePath` в существующий Worker config, который уже используется в этой ветке.

После будущего merge с веткой Stage-разделения настройка может быть перенесена или продублирована в `appsettings.Stage.json` отдельным follow-up.

---

## Required Design

Не вставлять fallback напрямую большим блоком в `RunCrawlerUseCase`.

Нужно выделить discovery источники.

Рекомендуемый интерфейс:

```csharp
public interface IProductUrlDiscoverySource
{
    Task<IReadOnlyCollection<Uri>> DiscoverProductUrlsAsync(CancellationToken cancellationToken);
}
```

Рекомендуемые реализации:

```text
SitemapProductUrlDiscoverySource
CategoryProductUrlDiscoverySource
```

Рекомендуемый оркестратор:

```text
ProductUrlDiscoveryService
```

Ожидаемая логика:

```text
1. Попробовать sitemap discovery.
2. Если sitemap discovery успешен и дал product URLs — использовать sitemap URLs.
3. Если sitemap discovery failed with SitemapUnavailable — перейти к category fallback.
4. Если sitemap discovery успешен, но дал 0 product URLs — перейти к category fallback.
5. Category fallback читает seed categories из config/category-seed-urls.varus.json.
6. Category fallback загружает HTML страниц категорий.
7. Category fallback извлекает product URLs.
8. Product URLs проходят через существующий UrlFilter.
9. Если после фильтрации есть URLs — передать их в существующий crawler pipeline.
10. Если URLs нет — завершить controlled failure ProductUrlDiscoveryUnavailable.
```

---

## Category URL Extraction

Для страниц категорий нужно извлекать только ссылки на товары.

Базовый алгоритм:

```text
1. Загрузить HTML category page.
2. Найти href-ссылки.
3. Привести относительные ссылки к absolute URL через base URL https://varus.ua.
4. Оставить только ссылки внутри host varus.ua.
5. Удалить anchors/query noise, если существующий pipeline этого требует.
6. Применить существующий UrlFilter.
7. Удалить дубликаты.
```

Не делать полный recursive crawler всего сайта.

Не обходить все ссылки с главной страницы.

Работать только с seed category URLs.

---

## Validation Rules

Category seed validation:

```text
name:
  - required
  - trim
  - non-empty

url:
  - required
  - trim
  - absolute URI
  - scheme https
  - host varus.ua
```

Category page response validation:

```text
200 OK + text/html -> можно парсить
404 -> warning CategoryPageNotFound
403 -> warning CategoryPageForbidden
429 -> warning CategoryPageRateLimited
5xx -> warning CategoryPageServerError
empty body -> warning CategoryPageEmptyBody
non-html content -> warning CategoryPageInvalidContentType
```

---

## Failure Classification

Добавить явную классификацию fallback failure, если её ещё нет.

Пример enum:

```csharp
public enum ProductUrlDiscoveryFailureKind
{
    None,
    SitemapUnavailable,
    CategorySeedFileMissing,
    CategorySeedFileInvalid,
    CategorySeedFileEmpty,
    CategoryPageNotFound,
    CategoryPageForbidden,
    CategoryPageRateLimited,
    CategoryPageServerError,
    CategoryPageInvalidContentType,
    CategoryPageEmptyBody,
    NoProductUrlsFound
}
```

Не обязательно использовать именно это имя enum, но в логах должна быть понятная причина.

---

## Logging Requirements

При переходе на fallback:

```text
Sitemap discovery unavailable. Falling back to category seed URL discovery.
```

При загрузке seed file:

```text
Loading category seed URLs. Path=config/category-seed-urls.varus.json
```

При невалидной seed-записи:

```text
Category seed URL rejected. Name=...; Url=...; Reason=...
```

При загрузке категории:

```text
Loading category page. Name=...; Url=...
```

При успешном извлечении URLs:

```text
Category page processed. Name=...; Url=...; ExtractedUrls=...; AcceptedUrls=...
```

Если все fallback-источники не дали URL:

```text
Product URL discovery failed. No product URLs found from sitemap or category seed fallback.
```

Не логировать полный HTML.

Preview HTML, если нужен, ограничивать 512 или 1024 символами.

---

## Acceptance Criteria

### AC1 — config path

Worker поддерживает настройку:

```text
Crawler:CategorySeedUrlsFilePath
```

Путь может быть относительным к content root.

---

### AC2 — seed file format

Worker умеет читать файл:

```text
config/category-seed-urls.varus.json
```

формата:

```json
{
  "Crawler": {
    "CategorySeedUrls": [
      {
        "name": "Category name",
        "url": "https://varus.ua/category-url"
      }
    ]
  }
}
```

---

### AC3 — seed validation

Невалидные записи не ломают весь запуск, а логируются как warning.

Примеры невалидных записей:

```text
- empty name
- empty url
- relative url
- http instead of https
- non-varus.ua host
- malformed URL
```

---

### AC4 — deduplication

Дубликаты seed category URLs удаляются по normalized URL.

---

### AC5 — sitemap remains primary

Если sitemap discovery успешен и дал product URLs, category fallback не запускается.

---

### AC6 — fallback starts when sitemap unavailable

Если sitemap discovery завершился `SitemapUnavailable`, запускается category fallback.

---

### AC7 — fallback starts when sitemap returns zero product URLs

Если sitemap discovery формально успешен, но после фильтрации получено 0 product URLs, запускается category fallback.

---

### AC8 — category HTML parsing

Category fallback загружает HTML страниц категорий и извлекает product URLs из href-ссылок.

---

### AC9 — existing URL filter is reused

Извлечённые category product URLs проходят через существующий UrlFilter.

Не дублировать логику фильтрации.

---

### AC10 — no recursive site crawl

Fallback не должен рекурсивно обходить весь сайт.

Разрешено загружать только URL из `CategorySeedUrls`.

---

### AC11 — controlled failure

Если sitemap недоступен и category fallback не нашёл product URLs, Worker завершается controlled failure:

```text
ProductUrlDiscoveryUnavailable
```

или аналогичным понятным failure reason.

Run должен завершаться со статусом error, без неожиданного unhandled exception.

---

### AC12 — no Stage work

Не создавать и не менять `appsettings.Stage.json`, если Stage-конфигов нет в текущей ветке.

Не реализовывать разделение баз Dev/Stage.

---

## Non-goals

Не делать в этом тикете:

```text
- Stage database split
- appsettings.Stage.json creation if it does not already exist
- DB schema changes
- UI changes
- proxy support
- anti-ban logic
- User-Agent rotation
- recursive crawling from home page
- automatic category discovery from main menu
- browser automation
- JavaScript rendering
- parsing Magento internal APIs unless already trivially available
```

---

## Testing Requirements

Добавить unit-тесты.

Минимальный набор:

```text
1. CategorySeedUrlsFilePath resolves relative to content root.
2. Valid seed file is parsed.
3. Empty seed file / empty CategorySeedUrls -> CategorySeedFileEmpty or equivalent.
4. Invalid JSON -> CategorySeedFileInvalid or equivalent.
5. Empty name -> rejected with warning.
6. Invalid URL -> rejected with warning.
7. http URL -> rejected with warning.
8. non-varus.ua URL -> rejected with warning.
9. duplicate URLs -> deduplicated.
10. Sitemap success with URLs -> category fallback is not called.
11. SitemapUnavailable -> category fallback is called.
12. Sitemap success with 0 URLs -> category fallback is called.
13. Category HTML with product links -> product URLs extracted.
14. Category HTML with duplicate product links -> deduplicated.
15. Category page 404 -> warning and continue.
16. Category page 500 -> warning and continue.
17. All sources empty/unavailable -> controlled ProductUrlDiscoveryUnavailable failure.
```

HTTP must be mocked/faked.

No real requests to `varus.ua` in unit tests.

---

## Manual Validation

After implementation, run:

```bat
dotnet build
```

Run targeted tests, for example:

```bat
dotnet test PriceCrawler.Web.Tests\PriceCrawler.Web.Tests.csproj --filter "FullyQualifiedName~CategorySeed|FullyQualifiedName~ProductUrlDiscovery|FullyQualifiedName~RunCrawlerUseCase"
```

Then run local smoke test with required local configuration:

```bat
dotnet run --project PriceCrawler.Worker -- --once --job vegetables
```

Expected behavior:

```text
- If sitemap works: crawler uses sitemap.
- If sitemap is unavailable: crawler logs fallback to category seed discovery.
- If category pages produce URLs: crawler continues existing product pipeline.
- If no URLs are found: crawler exits with controlled ProductUrlDiscoveryUnavailable.
```

---

## Expected Result

PriceCrawler.Worker should no longer depend exclusively on sitemap availability.

If Varus breaks or removes sitemap again, Worker should be able to continue by using configured seed category pages as a controlled fallback source of product URLs.

The solution must keep sitemap as the primary source and use category fallback only when needed.
