const pluralRules = new Intl.PluralRules("ru-RU");

/**
 * Picks the Russian word form for a count (1 товар, 2 товара, 5 товаров).
 * @param count Number to agree with.
 * @param forms Word forms for the `one`, `few` and `many` plural categories.
 * @returns Matching form.
 */
function pluralRu(count: number, forms: { one: string; few: string; many: string }): string {
  const category = pluralRules.select(count);
  return category === "one" ? forms.one : category === "few" ? forms.few : forms.many;
}

/**
 * All Russian UI texts in one place. A Ukrainian file with the same shape comes later
 * (`satisfies Strings` will then catch a missing key).
 */
export const ru = {
  app: {
    name: "PriceCrawler",
    subtitle: "Цены Varus",
  },
  nav: {
    sectionMain: "Разделы",
    products: "Товары",
    analytics: "Аналитика",
    development: "Разработка",
    users: "Пользователи",
  },
  header: {
    toggleSidebar: "Свернуть или развернуть меню",
    openMenu: "Открыть меню",
    closeMenu: "Закрыть меню",
    kyivTime: "Киев",
    kyivTimeHint: "Все даты на сайте — по киевскому времени",
  },
  theme: {
    label: "Тема",
    light: "Светлая",
    dark: "Тёмная",
    system: "Как в системе",
  },
  apiStatus: {
    online: "API работает",
    offline: "API недоступен",
    checking: "Проверка API…",
  },
  products: {
    title: "Товары",
    description: "Каталог магазина по категориям. Список товаров со стрелками изменения цены — в MPC-102.",
    categoriesTitle: "Категории верхнего уровня",
    productCount: (count: number) =>
      `${count.toLocaleString("ru-RU")} ${pluralRu(count, { one: "товар", few: "товара", many: "товаров" })}`,
    empty: "Категорий пока нет — запустите catalog-refresh.",
  },
  analytics: {
    title: "Аналитика",
    description: "Конструктор отчётов: метрика, набор товаров, группировка, период (MPC-106).",
  },
  development: {
    title: "Разработка",
    description: "Запуски сборщика, ошибки и очередь — перенос /runs со старого сайта (MPC-105).",
  },
  users: {
    title: "Пользователи",
    description: "Вход, роли и члены семьи (MPC-101).",
  },
  common: {
    comingSoon: "Раздел в разработке",
    loadError: "Не удалось загрузить данные",
    retry: "Повторить",
  },
  notFound: {
    title: "Страница не найдена",
    backHome: "К товарам",
  },
} as const;

/** Shape every UI language file must have. */
export type Strings = typeof ru;
