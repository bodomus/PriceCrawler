# Web API нового сайта (MPC-99)

`PriceCrawler.Api` — HTTP API для нового фронтенда (React, MPC-100). Старый сайт `PriceCrawler.Web`
(MVC + Kendo) продолжает работать отдельно; оба читают одну базу.

## Запуск

```powershell
docker compose up -d postgres
dotnet run --project PriceCrawler.Api          # http://localhost:5280, окружение Development
```

- Документация и «песочница» запросов (только Development): `http://localhost:5280/scalar`,
  спецификация OpenAPI 3.1: `http://localhost:5280/openapi/v1.json`. Описания берутся из XML-комментариев.
- База по умолчанию — `varprice_stage` (`appsettings.json`). При старте API проверяет версию схемы в режиме
  `DatabaseSchema:StartupMode` (по умолчанию `ValidateOnly`: API схему не меняет) и пишет в лог
  `Database target: ...`. Неверная версия схемы останавливает запуск.
- CORS: разрешённые адреса фронтенда — `Cors:AllowedOrigins`. В Development это `http://localhost:5173`
  (Vite); без настройки кросс-доменные запросы запрещены.

## Эндпоинты

| Метод и путь | Что делает |
|---|---|
| `GET /api/v1/stores/{store}/categories?parentId=` | Один уровень дерева категорий магазина: без `parentId` — корни, с ним — дети. Только активные, в порядке сайта, с `hasChildren` и числом товаров в поддереве. |
| `GET /api/v1/stores/{store}/products?categoryId=&page=&pageSize=&sort=&filter=` | Страница активных товаров магазина с последней ценой (см. ниже). `categoryId` — категория вместе со всеми подкатегориями. |
| `GET /health` | Процесс жив (зависимости не проверяются). |
| `GET /health/ready` | База отвечает (`select 1`). |

Ответы — JSON в camelCase, даты — ISO 8601 в UTC. Ошибки — RFC 9457 `application/problem+json`; неверный запрос — 400 с полем
`errors` по именам параметров:

```json
{ "title": "The request is invalid.", "status": 400,
  "errors": { "Store": ["Store must be 1-50 characters: lowercase Latin letters, digits or '-'."] } }
```

## Пагинация, сортировка, фильтр (вместо Kendo `DataSourceRequest`)

Общий контракт для всех списков (`Application/Features/Listing`):

| Параметр | Формат | По умолчанию |
|---|---|---|
| `page` | номер страницы с 1 | 1 |
| `pageSize` | 1…200 | 50 |
| `sort` | поля через запятую, `-` — по убыванию: `sort=-price,name` | у каждого списка свой (товары — `name`) |
| `filter` | `поле:оператор:значение`, параметр повторяется, условия через AND: `filter=name:contains:молоко&filter=price:lte:50` | нет |

Операторы: текст — `eq`, `ne`, `contains`, `startswith` (последние два без учёта регистра, `%` и `_`
ищутся буквально); числа и даты (ISO 8601, без зоны = UTC) — `eq`, `ne`, `gt`, `gte`, `lt`, `lte`;
булевы — `eq`, `ne`. Пустые значения (`null`) при сортировке всегда в конце; последним ключом сортировки
неявно идёт id, поэтому страницы стабильны. Не больше 10 условий сортировки и 10 фильтров.

Ответ списка:

```json
{ "items": [ ... ], "page": 2, "pageSize": 50, "totalCount": 10533, "totalPages": 211 }
```

Каждый список объявляет свои поля в `ListFieldSet` (имя, тип, можно ли сортировать и фильтровать);
неизвестное поле, неподходящий оператор или неверное значение — 400 с понятным текстом. В SQL поле
превращается только через белый список колонок (`SqlListBuilder`), значения всегда идут параметрами.

Поля списка товаров: `name`, `externalId` (только фильтр), `price`, `oldPrice`, `isPromo`, `inStock`,
`capturedAt`. Товар попадает в список, если его строка `product_catalog` активна и он хотя бы раз собран
(`product.url = product_catalog.normalized_url`); цена — из последнего `price_snapshot`.

## OpenAPI и типы для фронтенда

При каждой сборке `PriceCrawler.Api` пишет спецификацию в `PriceCrawler.Api/openapi/pricecrawler-api.json`
(пакет `Microsoft.Extensions.ApiDescription.Server`; база при этом не нужна). Файл лежит в git: изменился
контракт — изменился файл в PR. Числа в JSON строгие (`integer`, а не `integer | string`), чтобы
сгенерированные типы были обычными `number`. Фронтенд генерирует типы из этого файла, например
`npx openapi-typescript ../PriceCrawler.Api/openapi/pricecrawler-api.json -o src/api/schema.ts` (MPC-100).

## Прочее

Деревья категорий у магазинов разные, поэтому магазин (`store`, он же `category.source`, например `varus`)
входит в путь каждого запроса к каталогу.

## Архитектура

```text
HTTP ── PriceCrawler.Api (Minimal API: эндпоинт -> запрос -> ISender)
          │
          ▼
        PriceCrawler.Mediator (свой медиатор: pipeline Logging -> Validation -> handler)
          │
          ▼
        PriceCrawler.Application/Features/<Область>/<Сценарий> (запрос, валидатор, обработчик, DTO)
          │  порты из Domain (ICategoryRepository, ...)
          ▼
        PriceCrawler.Infrastructure (PostgreSQL: Pg*Repository)
```

- Эндпоинт ничего не знает о базе: он собирает запрос (`GetCategoryChildrenQuery`), отправляет его через
  `ISender.SendAsync` и возвращает результат.
- CQRS: чтение — `IQuery<T>` + `IQueryHandler<,>`, изменение — `ICommand<T>`/`ICommand` +
  `ICommandHandler<,>`. На каждый запрос ровно один обработчик (второй — ошибка при регистрации).
- Сквозная логика — `IPipelineBehavior<,>`, выполняется в порядке регистрации (первый — внешний):
  `LoggingBehavior` (время, ошибки; отказ валидации — без стека) и `ValidationBehavior` (все
  `IRequestValidator<T>` запроса; при нарушениях — `RequestValidationException`, API отвечает 400).
- API регистрирует только слой хранения (`AddPriceCrawlerPersistence`), без краулера и его настроек.

### Свой медиатор, а не MediatR

`PriceCrawler.Mediator` — около 500 строк (вместе с XML-комментариями) без зависимостей от остального решения. Обработчики и валидаторы
регистрируются сканированием сборки (`AddMediator(assembly)`), поведения — явно (`AddLogging()`,
`AddValidation()`, `AddOpenBehavior(typeof(...<,>))`). Рефлексия выполняется один раз на тип запроса: дальше
используется закэшированный типизированный диспетчер.

### Как добавить сценарий

1. `PriceCrawler.Application/Features/<Область>/`: `record XxxQuery(...) : IQuery<XxxDto>`, DTO,
   `XxxQueryHandler : IQueryHandler<XxxQuery, XxxDto>`, при необходимости `XxxQueryValidator : IRequestValidator<XxxQuery>`.
   Регистрировать ничего не нужно — сканер найдёт сам.
2. Если нужен новый доступ к данным — порт в `Domain/Interfaces`, реализация в `Infrastructure/Persistence`,
   регистрация в `AddPriceCrawlerPersistence`.
3. `PriceCrawler.Api/Endpoints/XxxEndpoints.cs`: `MapXxxEndpoints(this RouteGroupBuilder group)` и вызов в `Program.cs`.
4. Если это список — объявить поля в `ListFieldSet`, принять `ListParameters`, в SQL использовать `SqlListBuilder`.
5. Тесты в `PriceCrawler.Api.Tests`: HTTP без базы и интеграционный на PostgreSQL.

## Тесты

- `PriceCrawler.Mediator.Tests` — диспетчеризация, порядок поведений, валидация, отсутствие обработчика, регистрация.
- `PriceCrawler.Api.Tests`:
  - `CategoryEndpointsTests` — HTTP-уровень без базы (фейковый `ICategoryRepository`, проверка схемы отключена);
  - `PostgresIntegrationTests` (`Category=Integration`) — настоящий PostgreSQL на сервере `varprice_test`
    (`localhost:55432`, переопределяется переменной `PRICECRAWLER_TEST_POSTGRES`). Для каждого запуска
    создаётся отдельная база `pricecrawler_api_test_<guid>`: схему v2 создаёт сам API при старте (`Ensure`
    в окружении `Test`), затем заливаются тестовые данные, после — база удаляется. Отдельная база нужна,
    потому что тесты `PriceCrawler.Web.Tests` параллельно очищают `varprice_test` (`truncate`).

## Дальше

История цен товара, аналитика, Identity с ролями — отдельные задачи вехи MPC-95.
