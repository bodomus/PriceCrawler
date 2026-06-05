# MPC-57: Varus — добавить устойчивое обнаружение и валидацию sitemap

## Goal

Сделать загрузку sitemap в VarPrice.Worker устойчивой к битым ссылкам в `robots.txt`, HTML-ответам вместо XML, `404/403/429/5xx`, изменению sitemap URL и soft-404 страницам.

Сейчас Worker использует sitemap URL:

```text
https://varus.ua/sitemap-index.xml
```

Этот URL указан в:

```text
https://varus.ua/robots.txt
```

но сам URL может возвращать:

```http
HTTP/1.1 404 Not Found
Content-Type: text/html; charset=UTF-8
```

и HTML-страницу Magento `404 Not Found` вместо XML sitemap.

Worker должен корректно классифицировать такую ситуацию как недоступный/невалидный sitemap, а не пытаться работать с HTML как с XML.

---

## Problem

Текущий лог:

```text
Start processing HTTP request GET https://varus.ua/sitemap-index.xml
Received HTTP response headers ... - 404
Failed to load sitemap XML. Url=https://varus.ua/sitemap-index.xml, StatusCode=404, ContentEncoding=, Preview=<!doctype html>...
run_id=71; status=error; processed=0; errors=1
```

Фактический ответ сайта:

```http
HTTP/1.1 404 Not Found
Content-Type: text/html; charset=UTF-8
x-powered-by: PHP/8.3.31
x-magento-tags: ...
<title>404 Not Found</title>
```

При этом `robots.txt` всё ещё содержит:

```text
Sitemap: https://varus.ua/sitemap-index.xml
```

Вывод: внешний сайт может публиковать битую sitemap-ссылку. Crawler должен быть защищён от этого сценария.

---

## Required Design

Не делать точечный фикс вида “заменить URL”.

Нужно реализовать развязанную схему:

```text
ISitemapUrlProvider
ISitemapHttpClient
ISitemapResponseValidator
SitemapDiscoveryService / SitemapLoader
```

Рекомендуемая ответственность:

### ISitemapUrlProvider

Отвечает за список sitemap-кандидатов.

Источники:

```text
1. Sitemap URL из конфигурации, если задан.
2. Sitemap URL из robots.txt.
3. Known fallback candidates.
```

Fallback candidates минимум:

```text
https://varus.ua/sitemap.xml
https://varus.ua/sitemap_index.xml
https://varus.ua/sitemap-index.xml
```

### ISitemapHttpClient

Отвечает только за HTTP-загрузку sitemap-кандидата.

Должен возвращать response metadata:

```text
Url
StatusCode
ContentType
ContentEncoding
BodyPreview
Raw body / stream
```

### ISitemapResponseValidator

Отвечает за проверку ответа.

Должен валидировать:

```text
- HTTP status code
- Content-Type
- empty body
- HTML вместо XML
- XML parseability
- XML root element
```

Допустимые root elements:

```text
sitemapindex
urlset
```

### SitemapLoader / SitemapDiscoveryService

Оркестрирует процесс:

```text
1. Получает список sitemap-кандидатов.
2. Пробует каждый URL по очереди.
3. Валидирует ответ.
4. Первый валидный sitemap отдаёт дальше в существующий pipeline.
5. Все невалидные попытки логирует как warning.
6. Если валидный sitemap не найден — завершает crawler run контролируемой ошибкой.
```

---

## Failure Classification

Добавить явную классификацию ошибки sitemap.

Пример enum:

```csharp
public enum SitemapLoadFailureKind
{
    None,
    NotFound,
    Forbidden,
    RateLimited,
    ServerError,
    InvalidContentType,
    HtmlInsteadOfXml,
    InvalidXml,
    EmptyBody,
    UnexpectedStatusCode
}
```

Маппинг:

```text
404 -> NotFound
403 -> Forbidden
429 -> RateLimited
5xx -> ServerError
200 + text/html -> HtmlInsteadOfXml
200 + body starts with <!doctype html> or <html -> HtmlInsteadOfXml
200 + invalid XML -> InvalidXml
200 + empty body -> EmptyBody
non-200 unknown -> UnexpectedStatusCode
```

---

## Logging Requirements

При каждой неудачной попытке загрузки sitemap писать warning.

Формат должен быть диагностируемым:

```text
Sitemap candidate rejected.
Url=https://varus.ua/sitemap-index.xml;
StatusCode=404;
FailureKind=NotFound;
ContentType=text/html; charset=UTF-8;
ContentEncoding=;
Preview=<!doctype html>...
```

Если все кандидаты невалидны:

```text
Sitemap discovery failed. No valid sitemap found.
TriedUrls=...
FailureKinds=...
```

Финальный run должен завершаться контролируемо:

```text
run_id=N; status=error; processed=0; errors=1
```

Но причина должна быть понятной:

```text
SitemapUnavailable
```

---

## Acceptance Criteria

### AC1 — robots.txt parsing

Worker читает `https://varus.ua/robots.txt`.

Извлекает все строки вида:

```text
Sitemap: <url>
```

URL из `robots.txt` добавляются в список sitemap-кандидатов.

---

### AC2 — fallback candidates

Если sitemap из `robots.txt` невалиден, Worker пробует fallback-кандидаты:

```text
https://varus.ua/sitemap.xml
https://varus.ua/sitemap_index.xml
https://varus.ua/sitemap-index.xml
```

Дубликаты URL должны быть удалены.

---

### AC3 — 404 HTML не считается sitemap

Ответ:

```http
HTTP/1.1 404 Not Found
Content-Type: text/html; charset=UTF-8
```

с телом:

```html
<!doctype html>
<html>
<head>
<title>404 Not Found</title>
```

должен быть классифицирован как:

```text
FailureKind=NotFound
```

или, если статус 200 при HTML-странице:

```text
FailureKind=HtmlInsteadOfXml
```

Такой ответ нельзя передавать в XML-парсер как валидный sitemap.

---

### AC4 — soft-404 HTML

Ответ:

```http
HTTP/1.1 200 OK
Content-Type: text/html; charset=UTF-8
```

с HTML-страницей `404 Not Found` должен быть классифицирован как:

```text
HtmlInsteadOfXml
```

---

### AC5 — валидный sitemapindex

Ответ:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <sitemap>
    <loc>https://varus.ua/sitemap-products.xml</loc>
  </sitemap>
</sitemapindex>
```

должен считаться валидным.

---

### AC6 — валидный urlset

Ответ:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <url>
    <loc>https://varus.ua/product/example</loc>
  </url>
</urlset>
```

должен считаться валидным.

---

### AC7 — invalid XML

Ответ с `Content-Type: application/xml`, но с невалидным XML должен быть классифицирован как:

```text
InvalidXml
```

---

### AC8 — controlled failure

Если все sitemap-кандидаты невалидны, Worker не должен падать неожиданным exception.

Он должен:

```text
- записать понятный error log
- завершить crawler_run со статусом error
- указать причину SitemapUnavailable
- processed=0
- errors=1 или больше, в зависимости от текущей модели ошибок
```

---

## Non-goals

Не реализовывать в этом тикете:

```text
- парсинг категорий как fallback вместо sitemap
- обход сайта через HTML-категории
- прокси
- антибан-логику
- ротацию User-Agent
- изменение схемы product/price_snapshot
- изменение UI
```

---

## Testing Requirements

Добавить unit-тесты.

Минимальный набор:

```text
1. robots.txt содержит Sitemap URL — URL извлекается.
2. robots.txt не содержит Sitemap URL — используются fallback candidates.
3. Дубликаты sitemap URL удаляются.
4. 404 + HTML Magento page -> NotFound.
5. 200 + HTML page -> HtmlInsteadOfXml.
6. 200 + valid sitemapindex XML -> valid.
7. 200 + valid urlset XML -> valid.
8. 200 + invalid XML -> InvalidXml.
9. 200 + empty body -> EmptyBody.
10. 500 -> ServerError.
11. 429 -> RateLimited.
12. Все кандидаты невалидны -> SitemapUnavailable controlled failure.
```

Если в проекте уже есть test infrastructure для Worker, использовать её.
Если нет — добавить минимальные unit-тесты без поднятия реального Varus и без сетевых запросов.

HTTP должен мокаться/fake-иться.

---

## Implementation Notes

Не выполнять реальные HTTP-запросы в unit-тестах.

Для проверки XML root element учитывать namespace:

```xml
<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
```

и root name без привязки к конкретному prefix.

Проверка HTML должна быть простой и надёжной:

```text
- Content-Type contains text/html
- body starts with <!doctype html
- body starts with <html
- body contains <title>404 Not Found</title>
```

BodyPreview должен быть ограничен по размеру, например:

```text
512 или 1024 символа
```

Не логировать весь HTML-ответ.

---

## Expected Result

После выполнения тикета Worker должен корректно обрабатывать ситуацию:

```text
robots.txt указывает на sitemap-index.xml,
но sitemap-index.xml возвращает Magento 404 HTML.
```

Ожидаемое поведение:

```text
- Worker логирует rejected sitemap candidate.
- Классифицирует ошибку как NotFound / HtmlInsteadOfXml.
- Пробует fallback candidates.
- Если валидный sitemap найден — продолжает работу.
- Если не найден — завершает run контролируемым SitemapUnavailable.
```

Главная цель: внешний сбой Varus не должен выглядеть как непонятная ошибка парсинга XML или падение Worker.
