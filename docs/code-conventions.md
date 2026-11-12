# Соглашения по коду (MPC-98)

Договорённости от 04.11.2026, закреплённые инструментами сборки, а не устно.
Источник правды для правил — `.editorconfig` и `Directory.Build.props`; этот документ объясняет их.

## Коротко

- Язык — последний (C# 14, `net10.0`). Используем новые возможности: file-scoped namespace,
  `new()` по типу, коллекционные выражения `[]`, `switch`-выражения, `using var`, первичные конструкторы,
  `[GeneratedRegex]`, `[LoggerMessage]`.
- Каждая функция (метод, конструктор, локальная функция с нетривиальной логикой) получает короткий
  XML-комментарий **на английском**: что делает, входы, выход.
- Правила из `.editorconfig` с уровнем `warning` проверяет компилятор. В Release (`TreatWarningsAsErrors`)
  любое предупреждение — ошибка сборки, поэтому CI ловит нарушения.
- Старый код приводим к правилам только там, где его меняем.

## XML-документация

Формат:

```csharp
/// <summary>Returns the current price of a product from its card page.</summary>
/// <param name="html">Product card HTML.</param>
/// <param name="cancellationToken">Cancels the parsing.</param>
/// <returns>The parsed price, or <see langword="null"/> when the card has no price.</returns>
/// <exception cref="FormatException">The price block is present but malformed.</exception>
public Task<decimal?> ParsePriceAsync(string html, CancellationToken cancellationToken);
```

- `<summary>` — одно-два предложения: что делает, а не как.
- `<param>` — на каждый параметр; `<returns>` — для не-`void`/не-`Task`; `<exception>` — на исключения,
  которые метод бросает намеренно.
- Для реализаций интерфейса и переопределений — `/// <inheritdoc/>`.
- Комментарии внутри кода тоже на английском; они объясняют «почему», а не пересказывают код.

Что проверяет сборка:

| Проект | `GenerateDocumentationFile` | CS1591 (нет XML-комментария у публичного члена) |
|---|---|---|
| Новые проекты | да | предупреждение в Debug, **ошибка в Release** |
| Существующие (Domain, Application, Infrastructure, Web, Worker) | да | отключено (`NoWarn`) |
| Тестовые (`*.Tests`) | да | отключено |

Компилятор проверяет только публичные члены; комментарии к `private`/`internal` — договорённость,
её смотрим на ревью. Битые XML-комментарии (CS1570–CS1574: неверный XML, `cref` или `param`)
ловятся во всех проектах. Список «существующих» проектов задан в `Directory.Build.props`
(`PriceCrawlerIsLegacyProject`); новый проект попадает под строгие правила автоматически.
XML-файл документации в publish не копируется (`PublishDocumentationFile=false`); проект API,
которому он нужен для OpenAPI, включает его у себя.

## Анализаторы

- `EnableNETAnalyzers`, `AnalysisLevel=latest` — во всех проектах.
- `EnforceCodeStyleInBuild=true` — правила стиля (IDExxxx) из `.editorconfig` проверяются при сборке.
- Новые проекты дополнительно используют набор `AnalysisMode=Recommended`. Заметные следствия:
  - CA1848 / CA1873 — логирование через source-generated `[LoggerMessage]`, а не `logger.LogInformation(...)`;
  - CA1305 — явная культура (`CultureInfo.InvariantCulture`) при форматировании и разборе чисел и дат;
  - CA1822 — методы без состояния делаем `static`.
- Отключать правило можно только точечно (`#pragma warning disable` с причиной или
  `[SuppressMessage(..., Justification = "...")]`), не глобально.

## Стиль (`.editorconfig`)

Проверяются при сборке (`warning`):

- `using` вне namespace, `System.*` первыми, без лишних `using` (IDE0005); file-scoped namespace (IDE0161);
- фигурные скобки у `if`/`for`/`foreach`/`while` всегда (IDE0011);
- явные модификаторы доступа (кроме членов интерфейсов) и их порядок (IDE0040, IDE0036); без `this.`;
- `var` только когда тип очевиден из правой части (`var list = new List<int>()`), иначе явный тип (IDE0007/IDE0008);
- `new()` по типу, коллекционные выражения (`[]` вместо `Array.Empty<T>()`), `switch`-выражения, `using var`;
- `readonly` для неизменяемых полей; без неиспользуемых параметров, приватных членов и присваиваний;
- форматирование (IDE0055): отступ 4 пробела, `{` с новой строки, LF, UTF-8;
- публичные члены в PascalCase (IDE1006).

Остаются подсказками IDE (`suggestion`): первичные конструкторы (IDE0290) и `[.. query]` вместо
`.ToList()` (IDE0305).

В существующих проектах скобки (IDE0011) и правила `var` пока подсказки: в старом коде ~50 `if` без скобок и
~570 `var` там, где тип не очевиден. Это задано отдельной секцией в конце `.editorconfig`; правим при изменении файла.

Проверить и поправить стиль локально:

```powershell
dotnet build PriceCrawler.sln -c Release          # то же, что CI
dotnet format style PriceCrawler.sln --verify-no-changes
dotnet format PriceCrawler.sln --include <файл.cs>  # поправить только свой файл
```

## Фронтенд

ESLint + Prettier + TSDoc подключаются в задаче каркаса фронтенда (веха MPC-95). Для `*.ts`, `*.tsx`,
`*.js`, `*.css` в `.editorconfig` уже задан отступ 2 пробела.
