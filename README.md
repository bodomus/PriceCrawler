# Magazine Price Crawler (Varus)

Веб-сервис для сбора цен на товары сети Varus. Берет ссылки из sitemap, извлекает карточки товаров, сохраняет снимки цен в Postgres и дает простую UI-кнопку запуска.

## Требования
- .NET SDK 9.0.311 (фиксируется `global.json`, подходит для сборки `net8.0`)
- Docker (если запускаете через `docker compose`)

## Запуск

### Вариант 1: Docker
```bash
docker compose up --build
```
Откройте:
- http://localhost:8080/ — кнопка запуска сбора
- http://localhost:8080/health — healthcheck

### Вариант 2: Локально
```bash
dotnet run --project VarPrice.Web
```
Убедитесь, что Postgres доступен и строка подключения задана в `VarPrice.Web/appsettings.json` или через переменные окружения `ConnectionStrings__Postgres`.

## Архитектура
- UI: Razor Pages (`VarPrice.Web/Pages`) — тонкий слой, запускающий `CrawlerRunner`.
- Логика сбора: `VarPrice.Web/Crawler` — интерфейсы (`ISitemapReader`, `IProductCardExtractor`) и реализации для чтения sitemap/парсинга карточек.
- Хранилище: `VarPrice.Web/Storage` — репозиторий `ICrawlerRepository`, фабрика соединений и bootstrap схемы БД.
- Конфигурация: `Crawler`-секция в `VarPrice.Web/appsettings.json` и через переменные окружения.

## Горячие клавиши
- Visual Studio: `F5` (Debug), `Ctrl+F5` (Run без отладки)
- Rider: `Shift+F10` (Run), `Shift+F9` (Debug)
- Браузер: `Ctrl+R` (обновить страницу)

## Как добавить паттерны
- Фильтр ссылок из sitemap: `VarPrice.Web/Crawler/CrawlerOptions.cs` (`VegetablesUrlContains`).
- Маркеры и парсинг: `VarPrice.Web/Crawler/VarusProductCardExtractor.cs`:
  - `TryMatchProductId` — маркеры/шаблоны product_id.
  - `PriceParser` — логика поиска цены/старой цены.
  - `PackParser` — вес/объем и единицы.
  - `CityParser` — разбор города из URL.
После изменений запустите сбор и проверьте результат в БД.

## Сборка Release
```bash
dotnet publish --project VarPrice.Web -c Release
```

## Структура папок
- `VarPrice.Web/` — веб-приложение (UI + логика сбора + доступ к БД)
- `VarPrice.Web/Crawler/` — правила обхода и парсинга
- `VarPrice.Web/Storage/` — доступ к Postgres
- `schema.sql` — исходная схема (для справки)
- `docker-compose.yml`, `Dockerfile` — контейнеризация
