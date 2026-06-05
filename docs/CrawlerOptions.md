# CrawlerOptions

## Назначение

`CrawlerOptions` содержит настройки, управляющие discovery URL товаров, фильтрацией найденных ссылок, ограничениями запуска, HTTP-throttling, retry-поведением и circuit breaker для обращений к Varus.

## Где используется

Класс привязывается из секции `Crawler` в `appsettings*.json` через `IOptions<CrawlerOptions>`. Его читают discovery strategy, фильтр URL, координатор запросов, extractor карточек и основной `RunCrawlerUseCase`.

## Поля

| Поле | Тип | Описание |
|---|---:|---|
| `UrlFilterFilePath` | `string` | Путь к JSON-файлу с правилами исключения URL из обхода. Используется при загрузке `UrlFilterOptions`; неверный путь ломает загрузку фильтров. |
| `DiscoveryMode` | `string` | Режим discovery URL товаров: `CategorySeeds`, `Api` или `Sitemap`. Пустое значение приводит к режиму по умолчанию, неподдерживаемое значение завершает запуск ошибкой конфигурации. |
| `CategorySeedUrlsFilePath` | `string` | Путь к JSON-файлу со стартовыми URL категорий Varus. Используется category-seed discovery; неверный путь делает этот источник недоступным. |
| `SitemapIndexUrl` | `string` | Абсолютный URL sitemap index Varus. Используется только при выбранной sitemap discovery strategy. |
| `VegetablesUrlContains` | `string` | Подстрока, по которой `ProductUrlFilter` оставляет URL нужной категории. Пустое значение отключает этот фильтр. |
| `UseStubProductCardExtractor` | `bool` | Включает временную no-network заглушку вместо реального скачивания карточек товаров. При `true` crawler не делает HTTP-запросы к страницам товаров и сохраняет синтетические карточки по URL. |
| `MaxProductsPerRun` | `int` | Максимальное количество товаров, которое crawler берет в обработку за один запуск. Работает вместе с `MaxUrls` как защитный лимит. |
| `MaxUrls` | `int` | Верхний предел URL-кандидатов, собираемых discovery и передаваемых дальше. Увеличение повышает полноту сбора, но увеличивает время discovery и размер очереди. |
| `MaxCategoryPagesPerSeed` | `int` | Максимальное количество страниц пагинации для одной стартовой категории. Защищает от бесконечного обхода и слишком длинных категорий. |
| `MaxConcurrency` | `int` | Максимальное число URL, обрабатываемых параллельно при drain очереди. Увеличение ускоряет обработку, но повышает нагрузку на сайт, сеть и базу данных. |
| `RequestsPerSecond` | `double` | Целевой лимит HTTP-запросов к Varus в секунду. Используется `VarusRequestCoordinator` для throttling. |
| `RequestTimeoutSeconds` | `int` | Таймаут одного HTTP-запроса в секундах. Слишком малое значение увеличит число timeout-ошибок, слишком большое замедлит восстановление после зависаний. |
| `JitterDelayMsMin` | `int` | Минимальная случайная задержка между запросами в миллисекундах. Должна быть не больше `JitterDelayMsMax`. |
| `JitterDelayMsMax` | `int` | Максимальная случайная задержка между запросами в миллисекундах. Увеличение делает обход мягче, но дольше. |
| `RetryCount` | `int` | Количество повторных попыток HTTP-запроса после временной ошибки. Большее значение повышает шанс успеха, но увеличивает задержки и нагрузку. |
| `RetryBaseDelayMs` | `int` | Базовая задержка перед повторной попыткой HTTP-запроса в миллисекундах. Используется при расчете backoff. |
| `BreakerFailureThreshold` | `int` | Число подряд идущих отказов, после которого circuit breaker временно открывается. Защищает от массовых ошибок и rate limit. |
| `BreakerOpenSeconds` | `int` | Время открытого состояния circuit breaker в секундах. После достижения порога ошибок новые запросы временно откладываются. |

## Пример конфигурации

```json
{
  "Crawler": {
    "UrlFilterFilePath": "config/url-filters.json",
    "DiscoveryMode": "CategorySeeds",
    "CategorySeedUrlsFilePath": "config/category-seed-urls.varus.json",
    "SitemapIndexUrl": "https://varus.ua/sitemap-index.xml",
    "VegetablesUrlContains": "/ovochi",
    "UseStubProductCardExtractor": true,
    "MaxProductsPerRun": 200,
    "MaxUrls": 20000,
    "MaxCategoryPagesPerSeed": 10,
    "MaxConcurrency": 4,
    "RequestsPerSecond": 2.0,
    "RequestTimeoutSeconds": 15,
    "JitterDelayMsMin": 50,
    "JitterDelayMsMax": 250,
    "RetryCount": 2,
    "RetryBaseDelayMs": 500,
    "BreakerFailureThreshold": 20,
    "BreakerOpenSeconds": 60
  }
}
```

## Примечания

- Не увеличивать лимиты, concurrency и requests per second без понимания нагрузки на Varus и базу данных.
- Для тестовых запусков использовать небольшие значения `MaxProductsPerRun`, `MaxUrls` и `MaxCategoryPagesPerSeed`.
- Настройки retry, throttling и circuit breaker связаны между собой: агрессивные retry при высоком RPS могут усилить rate limit.
- `UseStubProductCardExtractor=true` отключает только загрузку карточек товаров; discovery категорий или sitemap может по-прежнему делать сетевые запросы.
