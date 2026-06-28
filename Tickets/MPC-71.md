# MPC-71 - Живой прогресс discovery в верхней console dashboard панели

## Проблема

Сейчас `CrawlerConsoleDashboard` отображается сверху консоли, но во время `catalog-refresh` данные почти не обновляются.

На этапе `Обнаружение товаров` crawler уже обрабатывает category seed страницы и в логах пишет:

- имя категории;
- URL категории;
- номер страницы;
- количество найденных товаров;
- количество новых товаров.

Но progress dashboard сверху не получает эти промежуточные данные. Из-за этого пользователь видит:

- `Обнаружено: 0`
- `Новых: -`
- `Обновлено: -`
- `Выполнение: 0.0%`

Хотя crawler реально уже работает.

## Цель

Во время обработки category seed страниц верхняя панель должна обновляться в реальном времени:

- показывать текущую категорию;
- показывать текущую страницу;
- увеличивать `Обнаружено`;
- показывать примерный прогресс discovery;
- не ждать окончания всего `DiscoverProductUrlsAsync`.

## Что нужно сделать

### 1. Расширить progress reporter

Добавить в `ICrawlerProgressReporter` методы для discovery progress, например:

```csharp
void SetDiscoveryProgress(
    int processedSeeds,
    int totalSeeds,
    int discoveredProductUrls,
    string currentSeedName,
    string currentSeedUrl,
    int currentPageNumber);
```

### 2. Передавать progress reporter в discovery strategy/source

В category seed discovery вызывать progress update:

- при начале обработки seed;
- после каждой страницы категории;
- при переходе на следующий seed.

### 3. Обновить dashboard state/render

Верхняя console dashboard панель должна отображать:

- текущий этап `Обнаружение товаров`;
- текущую категорию;
- текущую страницу;
- `Обнаружено` = текущее количество уникальных найденных product URLs;
- примерный progress discovery по обработанным seed страницам.

### 4. Сохранить совместимость

- Для non-category discovery режимов progress reporter должен оставаться безопасным no-op или показывать только общий discovery stage.
- Не менять высокоуровневый crawler flow.
- Не увеличивать агрессивность crawler запросов.

## Критерии приемки

- Во время `catalog-refresh` dashboard начинает обновляться до завершения всего discovery.
- При обработке category seed отображаются текущая категория и страница.
- `Обнаружено` растет по мере нахождения уникальных product URLs.
- Progress не остается `0.0%` на всем этапе discovery, если seeds уже обрабатываются.
- Существующие команды worker и dashboard продолжают работать.

## Validation

- Запустить focused tests для progress reporter / category discovery.
- Запустить `dotnet build`.
