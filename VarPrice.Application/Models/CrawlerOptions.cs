namespace VarPrice.Application.Models;

public sealed class CrawlerOptions
{
    /// <summary>
    /// Путь к JSON-файлу с правилами исключения URL из обхода.
    /// Используется при загрузке `UrlFilterOptions`, чтобы убрать из discovery нерелевантные или опасные для запуска ссылки.
    /// Ожидается относительный путь от content root приложения или абсолютный путь; изменение влияет на набор URL, попадающих в очередь.
    /// </summary>
    public string UrlFilterFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Режим обнаружения URL товаров.
    /// Используется фабрикой discovery strategy для выбора источника: `CategorySeeds`, `Api` или `Sitemap`.
    /// Если значение пустое, применяется режим по умолчанию; некорректное значение останавливает запуск с ошибкой конфигурации.
    /// </summary>
    public string DiscoveryMode { get; set; } = ProductUrlDiscoveryModes.CategorySeeds;

    /// <summary>
    /// Путь к JSON-файлу со стартовыми URL категорий Varus.
    /// Используется category-seed discovery для чтения списка категорий, с которых начинается сбор ссылок на товары.
    /// Ожидается относительный или абсолютный путь к файлу; неверный путь делает category-seed discovery недоступным.
    /// </summary>
    public string CategorySeedUrlsFilePath { get; set; } = string.Empty;

    /// <summary>
    /// URL sitemap index Varus.
    /// Используется только при выбранной sitemap discovery strategy для загрузки sitemap и извлечения URL товаров.
    /// Ожидается абсолютный HTTP/HTTPS URL; изменение переключает источник sitemap-данных.
    /// </summary>
    public string SitemapIndexUrl { get; set; } = string.Empty;

    /// <summary>
    /// Подстрока, по которой фильтр оставляет URL нужной товарной категории.
    /// Используется `ProductUrlFilter` после discovery, обычно для ограничения запуска овощным разделом.
    /// Пустое значение отключает этот фильтр; слишком узкое значение может отбросить все найденные URL.
    /// </summary>
    public string VegetablesUrlContains { get; set; } = string.Empty;

    /// <summary>
    /// Включает временную no-network заглушку вместо реального скачивания карточек товаров.
    /// Используется DI при выборе реализации `IProductCardExtractor`; при `true` crawler не делает HTTP-запросы к страницам товаров,
    /// а возвращает синтетическую карточку по URL. Ожидается временное значение `true` для тестов без нагрузки на Varus
    /// и `false` для реального сбора цен. Discovery категорий и sitemap этим флагом не отключаются.
    /// </summary>
    public bool UseStubProductCardExtractor { get; set; }

    /// <summary>
    /// Максимальное количество товаров, которое crawler возьмет в обработку за один запуск.
    /// Используется вместе с `MaxUrls` как защитный лимит перед постановкой URL в очередь.
    /// Увеличение повышает объем работы за запуск и нагрузку на сайт, очередь и базу данных.
    /// </summary>
    public int MaxProductsPerRun { get; set; } = 200;

    /// <summary>
    /// Максимальное количество URL-кандидатов, которое discovery может собрать или передать дальше.
    /// Используется как общий верхний предел для sitemap/category discovery и дополнительно ограничивает итоговый набор вместе с `MaxProductsPerRun`.
    /// Увеличение позволяет обрабатывать больше каталога, но повышает длительность discovery и размер очереди.
    /// </summary>
    public int MaxUrls { get; set; } = 20_000;

    /// <summary>
    /// Максимальное количество страниц пагинации, которое category-seed discovery обходит для одной стартовой категории.
    /// Используется как жесткая защита от бесконечной пагинации и чрезмерно длинных категорий.
    /// Значение меньше 1 в коде приводится к безопасному минимуму; увеличение повышает полноту сбора и нагрузку.
    /// </summary>
    public int MaxCategoryPagesPerSeed { get; set; } = 10;

    /// <summary>
    /// Максимальное количество URL из очереди, обрабатываемых параллельно в рамках запуска.
    /// Используется `RunCrawlerUseCase` при drain очереди через `Parallel.ForEachAsync`.
    /// Увеличение ускоряет обработку, но повышает нагрузку на Varus, сеть, CPU и базу данных.
    /// </summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>
    /// Целевой лимит HTTP-запросов к Varus в секунду.
    /// Используется `VarusRequestCoordinator` для throttling между запросами discovery и карточек товаров.
    /// Низкое значение замедляет crawler, высокое повышает риск rate limit или блокировок.
    /// </summary>
    public double RequestsPerSecond { get; set; } = 2.0d;

    /// <summary>
    /// Таймаут одного HTTP-запроса к Varus в секундах.
    /// Используется HTTP-слоем crawler-а при загрузке страниц и карточек.
    /// Слишком малое значение увеличит число timeout-ошибок, слишком большое замедлит восстановление после зависших ответов.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Минимальная случайная задержка между запросами в миллисекундах.
    /// Используется координатором запросов как нижняя граница jitter, чтобы сделать обход менее резким.
    /// Должна быть не больше `JitterDelayMsMax`; увеличение снижает агрессивность crawler-а.
    /// </summary>
    public int JitterDelayMsMin { get; set; } = 50;

    /// <summary>
    /// Максимальная случайная задержка между запросами в миллисекундах.
    /// Используется координатором запросов как верхняя граница jitter вместе с `JitterDelayMsMin`.
    /// Увеличение делает обход мягче, но увеличивает общее время выполнения.
    /// </summary>
    public int JitterDelayMsMax { get; set; } = 250;

    /// <summary>
    /// Количество повторных попыток HTTP-запроса после временной ошибки.
    /// Используется extractor-ом и сетевыми компонентами crawler-а для transient-сбоев.
    /// Большее значение повышает шанс успешной обработки нестабильных страниц, но увеличивает задержки и нагрузку.
    /// </summary>
    public int RetryCount { get; set; } = 2;

    /// <summary>
    /// Базовая задержка перед повторной попыткой HTTP-запроса в миллисекундах.
    /// Используется при расчете backoff между retry-попытками.
    /// Увеличение снижает давление на источник после ошибок, но замедляет обработку проблемных URL.
    /// </summary>
    public int RetryBaseDelayMs { get; set; } = 500;

    /// <summary>
    /// Количество подряд идущих отказов, после которого circuit breaker временно открывается.
    /// Используется `VarusRequestCoordinator` для защиты от массовых ошибок и rate limit.
    /// Меньшее значение быстрее ставит crawler на паузу, большее допускает больше неудачных запросов подряд.
    /// </summary>
    public int BreakerFailureThreshold { get; set; } = 20;

    /// <summary>
    /// Время открытого состояния circuit breaker в секундах.
    /// Используется после достижения `BreakerFailureThreshold`, чтобы временно остановить новые запросы.
    /// Увеличение дает источнику больше времени восстановиться, но удлиняет запуск при массовых ошибках.
    /// </summary>
    public int BreakerOpenSeconds { get; set; } = 60;
}
