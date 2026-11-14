# Фронтенд нового сайта (MPC-100)

`PriceCrawler.Client` — SPA нового сайта. Данные берёт только из `PriceCrawler.Api` (`docs/api.md`).

## Стек

| Что | Чем |
|---|---|
| Язык, сборка | TypeScript (strict, `noUncheckedIndexedAccess`, `exactOptionalPropertyTypes`), Vite 8 |
| UI | React 19, Tailwind CSS 4, компоненты shadcn/ui (Radix, иконки lucide) |
| Данные | TanStack Query 5, типизированный клиент `openapi-fetch` |
| Таблицы, деревья | TanStack Table 9 (`useTable`, только `rowSortingFeature`), react-arborist 4 (требует react-dnd как peer-зависимость, перетаскивание выключено) |
| Маршруты | React Router |
| Качество | ESLint (`typescript-eslint` strict type-checked), Prettier, Vitest + Testing Library, Playwright |

Внешний вид — как у next-shadcn-admin-dashboard: нейтральная палитра shadcn (`src/index.css`), светлая и тёмная
тема, боковое меню, фиксированная шапка. Код свой: компоненты shadcn лежат копиями в `src/components/ui`
(так задумано у shadcn), добавлять новые можно `npx shadcn@latest add <имя>` (`components.json` настроен).

TypeScript пока 5.9: `openapi-typescript` и `typescript-eslint` ещё не поддерживают 6/7.

## Запуск

Нужен Node.js 22.12+ (или 24).

```powershell
dotnet run --project PriceCrawler.Api   # http://localhost:5280
cd PriceCrawler.Client
npm install
npm run dev                             # http://localhost:5173
```

Dev-сервер Vite проксирует `/api` и `/health` на `http://localhost:5280` (другой адрес — переменная
`VITE_API_PROXY_TARGET`), поэтому в браузере всё с одного адреса. Если фронтенд и API живут на разных адресах,
при сборке задаётся `VITE_API_BASE_URL=https://api.example` (и этот адрес фронтенда — в `Cors:AllowedOrigins` API).

## Команды

| Команда | Что делает |
|---|---|
| `npm run dev` | dev-сервер с горячей перезагрузкой |
| `npm run build` | проверка типов + сборка в `dist/` |
| `npm run lint` / `npm run format` | ESLint / Prettier (`format:check` — только проверка) |
| `npm test` | модульные тесты Vitest (jsdom) |
| `npm run e2e` | smoke-тест Playwright по собранному сайту (`vite preview`, API подменяется в тесте); перед первым запуском `npx playwright install chromium` |
| `npm run api:types` | перегенерировать `src/api/schema.ts` из `PriceCrawler.Api/openapi/pricecrawler-api.json` |

CI (`.github/workflows/ci.yml`, job `client`) выполняет всё это и падает, если `schema.ts` не совпадает с
OpenAPI-файлом: поменял контракт API — запусти `npm run api:types` и закоммить оба файла.

## Структура

```text
src/
  api/          schema.ts (генерируется), client.ts (openapi-fetch, ApiError), queries.ts (queryOptions + ключи)
  app/          providers.tsx (Query, тема, подсказки), router.tsx (таблица маршрутов)
  components/
    layout/     AppLayout (меню + шапка + панель с прокруткой), AppSidebar, ThemeToggle, KyivClock, ApiStatus
    ui/         компоненты shadcn/ui
  hooks/        useTheme, useMediaQuery, useNow
  i18n/         ru.ts — все тексты интерфейса; index.ts — текущий язык (`t`)
  lib/          config (магазин, адрес API), kyiv-time, theme, utils (cn)
  pages/        страницы разделов
e2e/            smoke-тесты Playwright
```

## Договорённости

- **Тексты** — только в `src/i18n/ru.ts` (`t.раздел.ключ`), в компонентах строк на русском нет. Украинский
  появится файлом той же формы (`satisfies Strings`).
- **Время** — всегда по Киеву (`lib/kyiv-time.ts`, `Europe/Kyiv`), независимо от часового пояса браузера;
  API отдаёт даты в UTC.
- **Прокрутка** — страница не растёт: высота окна делится на шапку и панель `main`, прокручивается панель
  (и потом таблицы внутри неё).
- **Запросы** — через `queryOptions` в `api/queries.ts` с ключами из `queryKeys`; в компонентах `useQuery(xxxQuery(...))`.
  Ошибка API — `ApiError` со статусом и `title` из problem details.
- **Магазин** — деревья категорий у магазинов разные, магазин входит в каждый запрос; пока он один
  (`DEFAULT_STORE = "varus"`).
- У каждой функции — короткий JSDoc на английском (как XML-комментарии в C#).

## Экран «Товары» (MPC-102)

`src/pages/products/`:

- `category-tree.tsx` — дерево на react-arborist. Сначала грузится только верхний уровень; дети узла
  запрашиваются при первом раскрытии (`categoryChildrenQuery`, потом из кэша), пока идёт загрузка — строка
  «Загрузка…». Выбор узла фильтрует таблицу (категория вместе с подкатегориями).
- `products-table.tsx` — TanStack Table v9, сортировка серверная (клик по заголовку, Shift — добавить поле),
  колонки: товар (фасовка, «Акция», «Нет в наличии», ссылка на сайт), цена (старая зачёркнута), изменение за
  день (`price-change.tsx`: зелёная стрелка вниз — подешевело, красная вверх — подорожало), мини-график
  (`sparkline.tsx`, обычный SVG; ECharts — для большого графика в MPC-103), время обновления по Киеву.
  При группировке перед каждой группой — строка с названием и числом товаров.
- `pagination-bar.tsx` — 25/50/100/200 строк, переход по страницам; пока грузится следующая, видна предыдущая.
- `list-state.ts` — все параметры в адресе: `category`, `q` (поиск по названию, с задержкой 300 мс), `sort`,
  `group`, `size`, `page`. Любое изменение, кроме страницы, возвращает на первую.

## Разделы

| Меню | Сейчас | Тикет |
|---|---|---|
| Товары | дерево категорий + таблица товаров (MPC-102) | карточка товара — MPC-103 |
| Аналитика | заглушка | MPC-106 |
| Разработка | заглушка | MPC-105 |
| Пользователи | заглушка | MPC-101 |
