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
| `GET /health` | Процесс жив (зависимости не проверяются). |
| `GET /health/ready` | База отвечает (`select 1`). |

Ответы — JSON в camelCase. Ошибки — RFC 9457 `application/problem+json`; неверный запрос — 400 с полем
`errors` по именам параметров:

```json
{ "title": "The request is invalid.", "status": 400,
  "errors": { "Store": ["Store must be 1-50 characters: lowercase Latin letters, digits or '-'."] } }
```

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
4. Тест в `PriceCrawler.Api.Tests` (API в памяти, без базы, с фейковым репозиторием).

## Тесты

- `PriceCrawler.Mediator.Tests` — диспетчеризация, порядок поведений, валидация, отсутствие обработчика, регистрация.
- `PriceCrawler.Api.Tests` — HTTP-уровень через `WebApplicationFactory`: без базы (фейковый `ICategoryRepository`,
  проверка схемы при старте отключена).

## Дальше

Товары и история цен, аналитика, Identity с ролями — отдельные задачи вехи MPC-95.
