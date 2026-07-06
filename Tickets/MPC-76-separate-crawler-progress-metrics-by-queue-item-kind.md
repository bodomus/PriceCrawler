# MPC-76 - Separate crawler progress metrics by queue item kind

URL: https://bodomus.youtrack.cloud/issue/MPC-76

**#** MPC-74 **— Separate crawler progress metrics by queue item kind**

**## Context**

After MPC-72 and MPC-73, the crawler correctly distinguishes listing pages from product pages at the processing level, and the console dashboard no longer mixes discovery/catalog counters with queue counters.

However, queue progress metrics are still semantically incorrect.

The current progress model uses shared counters:

\- \`CheckedProducts\`

\- \`SuccessfulProducts\`

\- \`FailedProducts\`

These counters are incremented not only for \`ProductPage\`, but also for successfully or unsuccessfully processed \`ListingPage\` / \`CategoryPage\` queue items.

As a result, the console dashboard can show values that are mathematically consistent but misleading.

Example:

\`\`\`text

Обнаружено:              6 648

Новых:                   2 000 / 6 648

Обновлено:                   0 / 6 648

Выбрано на проверку:      2 000 / 6 648

Ссылок в очереди:         3 973

Обработано ссылок:        3 840 / 3 973

Успешно:                  3 333 / 3 973

Ошибок:                     507 / 3 973

Выполнение:               96.7%

\`\`\`

The arithmetic is valid:

\`\`\`text

3 333 successful

\+ 507 failed

= 3 840 processed

\`\`\`

and:

\`\`\`text

3 840 / 3 973 = 96.7%

\`\`\`

But the counters combine different queue item kinds:

\`\`\`text

ProductPage

\+

ListingPage

\+

CategoryPage

\`\`\`

Therefore:

\`\`\`text

SuccessfulProducts

\`\`\`

does not necessarily mean:

\`\`\`text

successfully checked products

\`\`\`

This task must fix the progress model itself, not only rename dashboard labels.

**---**

**## Problem**

The crawler has explicit queue item kinds, but progress metrics are not separated by queue item kind.

The current logic allows listing page processing to increment product-named counters.

Conceptually incorrect behavior:

\`\`\`csharp

await queueRepository.MarkSucceededAsync(item.Id, ct);

progressReporter.IncrementChecked();

progressReporter.IncrementSuccessful();

\`\`\`

The same counters are also used for real product pages.

This creates several problems:

1\. Product progress cannot be measured independently.

2\. Listing progress cannot be inspected independently.

3\. The main completion percentage does not answer the question: \`How many product pages have been checked?\`

4\. Dashboard labels and internal counter names no longer match their actual semantics.

5\. Future queue item kinds will make the ambiguity worse.

**---**

**## Goal**

Introduce explicit progress metrics for:

1\. catalog/discovery;

2\. listing/category queue processing;

3\. product queue processing;

4\. total queue processing.

The progress model must clearly distinguish:

\`\`\`text

ListingPage / CategoryPage

\`\`\`

from:

\`\`\`text

ProductPage

\`\`\`

The dashboard must show product progress independently from listing progress.

**---**

**## Required behavior**

**### 1. Do not use product-named counters for non-product queue items**

The following counters must no longer be incremented by listing/category processing:

\`\`\`text

CheckedProducts

SuccessfulProducts

FailedProducts

\`\`\`

\`ListingPage\` and \`CategoryPage\` processing must use dedicated listing counters.

**### 2. Introduce explicit counters**

Add progress counters equivalent to the following semantic model.

Exact naming may follow project conventions.

\`\`\`csharp

ProductQueueTotal

ProductProcessed

ProductSucceeded

ProductFailed

ListingQueueTotal

ListingProcessed

ListingSucceeded

ListingFailed

ProductLinksDiscoveredFromListings

\`\`\`

Optional total queue counters may be derived instead of stored:

\`\`\`csharp

TotalQueueItems = ProductQueueTotal + ListingQueueTotal;

TotalProcessedQueueItems = ProductProcessed + ListingProcessed;

\`\`\`

Do not duplicate counters if values can be safely derived.

**### 3. Count queue items by actual \`QueueItemKind\`**

Progress updates must be based on the queue item kind.

Expected routing semantics:

\`\`\`text

ProductPage

&nbsp;&nbsp;&nbsp;&nbsp;-\> ProductProcessed

&nbsp;&nbsp;&nbsp;&nbsp;-\> ProductSucceeded or ProductFailed

ListingPage

&nbsp;&nbsp;&nbsp;&nbsp;-\> ListingProcessed

&nbsp;&nbsp;&nbsp;&nbsp;-\> ListingSucceeded or ListingFailed

CategoryPage

&nbsp;&nbsp;&nbsp;&nbsp;-\> ListingProcessed

&nbsp;&nbsp;&nbsp;&nbsp;-\> ListingSucceeded or ListingFailed

\`\`\`

For now, \`CategoryPage\` may share the listing metrics because it uses the listing extraction flow.

Do not count \`ListingPage\` or \`CategoryPage\` as processed products.

**### 4. Product links discovered from listings**

When a listing page discovers product URLs:

\`\`\`text

found_product_links

\`\`\`

and the queue repository actually enqueues some subset:

\`\`\`text

enqueued_product_links

\`\`\`

the progress model must clearly distinguish these concepts.

Add or expose at least:

\`\`\`text

ProductLinksDiscoveredFromListings

\`\`\`

Prefer additionally exposing:

\`\`\`text

ProductLinksEnqueuedFromListings

\`\`\`

if both values are already available without additional database queries.

Important:

\`\`\`text

found links != enqueued links

\`\`\`

because deduplication or idempotency may prevent some URLs from being inserted.

Do not silently treat \`FoundCount\` as actual queue growth.

**### 5. Queue totals must represent actual queue growth**

The product queue total must increase only by the number of product queue items actually accepted/enqueued.

Example:

\`\`\`text

listing page found:       541 links

actually enqueued:          0 links

\`\`\`

Expected:

\`\`\`text

ProductLinksDiscoveredFromListings += 541

ProductLinksEnqueuedFromListings   += 0

ProductQueueTotal                  += 0

\`\`\`

Do not increase \`ProductQueueTotal\` by 541 in this case.

This is important because logs already show scenarios where a listing page finds hundreds of links but enqueues zero due to deduplication.

**### 6. Define the main completion percentage**

The primary dashboard completion percentage for the price-checking stage must represent product processing progress.

Required semantics:

\`\`\`text

ProductProcessed / ProductQueueTotal

\`\`\`

The main progress percentage must not be based on all queue item kinds combined.

Listing progress may be shown separately.

Fallback behavior for zero denominator must remain safe and return \`0%\` or the existing project convention.

**### 7. Dashboard output**

Update the console dashboard so that product and listing progress are visible separately.

Recommended layout:

\`\`\`text

Обнаружено товаров:              6 648

Выбрано из каталога:             2 000

Listing в очереди:               XXX

Listing обработано:              XXX / XXX

Listing успешно:                 XXX

Listing ошибок:                   XX

Найдено product URL:            XXXX

Добавлено product URL:          XXXX

Товаров в очереди:              XXXX

Товаров обработано:             XXXX / XXXX

Товаров успешно:                XXXX / XXXX

Ошибок товаров:                  XXX / XXXX

Текущий этап: Проверка товаров

Текущая ссылка: https://...

Выполнение товаров:              XX.X%

\`\`\`

The exact wording may be adjusted to fit console width, but semantic separation is mandatory.

Do not hide one type behind a generic label like:

\`\`\`text

Успешно

Ошибок

Обработано ссылок

\`\`\`

unless the label explicitly states whether the value is total queue, listing, or product.

**---**

**## Architecture requirements**

**### 1. Progress state must model semantic domains**

Do not solve this task only inside \`CrawlerConsoleDashboard\`.

The separation must exist in:

\`\`\`text

ICrawlerProgressReporter

CrawlerProgressState

CrawlerProgressSnapshot

queue processing progress calls

tests

dashboard rendering

\`\`\`

The dashboard must render an already-correct snapshot.

It must not reconstruct item kind statistics from ambiguous counters.

**### 2. Prefer explicit methods**

Recommended interface shape:

\`\`\`csharp

void IncrementProductProcessed();

void IncrementProductSucceeded();

void IncrementProductFailed();

void IncrementListingProcessed();

void IncrementListingSucceeded();

void IncrementListingFailed();

void IncrementProductLinksDiscoveredFromListings(int value);

void IncrementProductLinksEnqueuedFromListings(int value);

void IncrementProductQueueTotal(int value);

\`\`\`

Exact names may differ.

Avoid generic methods such as:

\`\`\`csharp

IncrementChecked();

IncrementSuccessful();

IncrementFailed();

\`\`\`

when the caller has to rely on undocumented context to know what is being counted.

**### 3. Keep updates thread-safe**

Queue items are processed in parallel.

All progress counters must remain safe under concurrent updates.

Use the existing thread-safe approach:

\`\`\`text

Interlocked

Volatile

lock only for shared text state

\`\`\`

Do not introduce non-atomic read-modify-write operations for counters.

**### 4. Reset behavior**

The existing reset behavior must reset all newly introduced counters.

Sequential workflows such as:

\`\`\`text

run-all

\`\`\`

must not inherit listing or product progress from a previous stage.

**---**

**## Processing rules**

**### ProductPage success**

Expected progress update:

\`\`\`text

ProductProcessed += 1

ProductSucceeded += 1

\`\`\`

**### ProductPage terminal failure**

Expected progress update:

\`\`\`text

ProductProcessed += 1

ProductFailed += 1

\`\`\`

**### ProductPage retry**

Do not count a retry attempt as a completed product.

The processed counter must increase only after terminal success or terminal failure.

**### ListingPage / CategoryPage success**

Expected progress update:

\`\`\`text

ListingProcessed += 1

ListingSucceeded += 1

\`\`\`

Additionally:

\`\`\`text

ProductLinksDiscoveredFromListings += result.FoundCount

ProductLinksEnqueuedFromListings   += enqueued

ProductQueueTotal                  += enqueued

\`\`\`

**### ListingPage / CategoryPage terminal failure**

Expected progress update:

\`\`\`text

ListingProcessed += 1

ListingFailed += 1

\`\`\`

**### Listing retry**

Do not count a retry attempt as a completed listing item.

**---**

**## Acceptance criteria**

**### AC1 — Listing success does not increment product counters**

Given:

\`\`\`text

QueueItemKind.ListingPage

\`\`\`

When the listing page is processed successfully,

Then:

\`\`\`text

ListingProcessed == 1

ListingSucceeded == 1

ProductProcessed == 0

ProductSucceeded == 0

\`\`\`

**### AC2 — Product success increments only product counters**

Given:

\`\`\`text

QueueItemKind.ProductPage

\`\`\`

When the product page is processed successfully,

Then:

\`\`\`text

ProductProcessed == 1

ProductSucceeded == 1

ListingProcessed == 0

ListingSucceeded == 0

\`\`\`

**### AC3 — Listing terminal failure does not increment product failures**

Given a listing item reaches terminal failure,

Then:

\`\`\`text

ListingProcessed == 1

ListingFailed == 1

ProductProcessed == 0

ProductFailed == 0

\`\`\`

**### AC4 — Product terminal failure does not increment listing failures**

Given a product item reaches terminal failure,

Then:

\`\`\`text

ProductProcessed == 1

ProductFailed == 1

ListingProcessed == 0

ListingFailed == 0

\`\`\`

**### AC5 — Retries do not inflate completed counters**

Given a queue item fails transiently and is scheduled for retry,

Then neither product nor listing processed/success/failure terminal counters are incremented yet.

Only terminal success or terminal failure may increment completed-item counters.

**### AC6 — Found links and enqueued links are different metrics**

Given a listing extractor returns:

\`\`\`text

FoundCount = 541

\`\`\`

and:

\`\`\`text

EnqueueAsync(...) returns 0

\`\`\`

Then:

\`\`\`text

ProductLinksDiscoveredFromListings += 541

ProductLinksEnqueuedFromListings   += 0

ProductQueueTotal                  += 0

\`\`\`

**### AC7 — Product queue grows only by actual enqueue count**

Given initial product queue total:

\`\`\`text

2000

\`\`\`

and listing processing actually enqueues:

\`\`\`text

1973

\`\`\`

Then:

\`\`\`text

ProductQueueTotal == 3973

\`\`\`

The total must not use raw discovered-link count when enqueue deduplication rejects links.

**### AC8 — Product progress percentage uses product counters**

Given:

\`\`\`text

ProductProcessed = 3840

ProductQueueTotal = 3973

\`\`\`

Then the main product completion percentage is:

\`\`\`text

96.7%

\`\`\`

Listing counters must not affect this result.

**### AC9 — Dashboard exposes listing and product metrics separately**

The console dashboard must make it possible to answer independently:

\`\`\`text

How many listing pages were processed?

How many listing pages succeeded?

How many listing pages failed?

How many product pages were processed?

How many product pages succeeded?

How many product pages failed?

How many product URLs were discovered from listings?

How many were actually enqueued?

\`\`\`

No inference from combined counters should be required.

**### AC10 — Reset clears all counters**

After \`Reset()\`:

\`\`\`text

ProductQueueTotal == 0

ProductProcessed == 0

ProductSucceeded == 0

ProductFailed == 0

ListingQueueTotal == 0

ListingProcessed == 0

ListingSucceeded == 0

ListingFailed == 0

ProductLinksDiscoveredFromListings == 0

ProductLinksEnqueuedFromListings == 0

\`\`\`

Text state must also be reset according to current behavior.

**---**

**## Tests required**

**### \`CrawlerProgressStateTests\`**

1\. Stores independent product counters.

2\. Stores independent listing counters.

3\. Concurrent increments remain correct.

4\. Reset clears all new counters.

5\. Product completion percentage uses product counters only.

**### \`PriceCollectionQueueProcessor\` tests**

1\. Product success increments only product counters.

2\. Product terminal failure increments only product failure counters.

3\. Product retry does not increment terminal counters.

4\. Listing success increments only listing counters.

5\. Listing terminal failure increments only listing failure counters.

6\. Listing retry does not increment terminal counters.

7\. Listing \`FoundCount\` and actual \`enqueued\` count are tracked separately.

8\. Product queue total grows by actual \`EnqueueAsync\` return value.

**### Dashboard tests**

Add formatter/render-oriented tests where practical.

At minimum verify that the snapshot exposes enough information so the dashboard does not need to infer listing/product separation from shared counters.

**---**

**## Non-goals**

Do not redesign the crawler queue persistence model.

Do not change retry policy.

Do not change HTTP throttling, RPS, backoff, anti-ban logic, or extractor behavior.

Do not change product parsing logic.

Do not redesign discovery strategy.

Do not add database queries on every dashboard refresh.

Do not calculate dashboard metrics by polling the database repeatedly.

Do not introduce a second independent source of truth for queue progress.

**---**

**## Migration / compatibility notes**

Existing counters with ambiguous names:

\`\`\`text

CheckedProducts

SuccessfulProducts

FailedProducts

QueueLinksRequested

\`\`\`

must be reviewed.

Preferred solution:

\- replace ambiguous counters with explicit product/listing counters;

\- update all call sites;

\- update snapshot model;

\- update dashboard;

\- update tests.

Temporary compatibility properties are acceptable only if they are derived from the new counters and clearly documented.

Do not keep two independently mutable counter systems for the same concept.

**---**

**## Logging requirements**

Keep structured logs.

For listing processing, preserve or add:

\`\`\`text

run_id

queue_id

url

page_kind

found_product_links

enqueued_product_links

\`\`\`

For product processing, preserve:

\`\`\`text

run_id

queue_id

url

page_kind

error_code

http_status

transient

\`\`\`

Progress counters must not be reconstructed by parsing logs.

**---**

**## Definition of done**

The task is complete when:

1\. Listing/category processing no longer increments product counters.

2\. Product processing no longer increments listing counters.

3\. Product and listing terminal success/failure metrics are independent.

4\. Retry attempts do not inflate terminal progress counters.

5\. Found product links and actually enqueued product links are tracked separately.

6\. Product queue total grows only by actual enqueue count.

7\. Main completion percentage represents product processing progress.

8\. Dashboard clearly separates listing and product metrics.

9\. Progress state remains thread-safe.

10\. Reset clears all new counters.

11\. Unit and integration tests cover the required cases.

12\. \`dotnet build VarPrice.sln\` passes.

13\. \`dotnet test VarPrice.sln\` passes.

**---**

**## Reviewer note**

This task is not a console-label cleanup.

The core defect is semantic:

\`\`\`text

queue item kind

\`\`\`

must be reflected in the progress model.

A solution that only renames:

\`\`\`text

CheckedProducts

SuccessfulProducts

FailedProducts

\`\`\`

to generic names, while continuing to aggregate listing and product processing into one set of counters, does not satisfy this ticket.
