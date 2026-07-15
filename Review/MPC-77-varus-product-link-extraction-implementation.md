# Implementation Report

## Ticket

MPC-77 — Fix false product URL extraction from VARUS listing pages.

## Workflow

- Level: 2.
- Graphify: existing graph refreshed and queried before implementation.
- CRG: updated before and after implementation; UTF-8 mode was required for console output.
- Working tree before changes: dirty. `.doc/Screenshot_4.png` and temporary `!!!` logging were
  pre-existing user changes. The user authorized removal of the warning and preservation of the image.

## Scope

- Projects: Infrastructure and Web.Tests; documentation/changelog at repository root.
- Database impact: none.
- Operational impact: listing/category discovery now enqueues only verified products.
- Public contract impact: none; interfaces, queue records, error codes, and counters are unchanged.

## Root cause and selected source

`CategoryProductLinkExtractor` tried approximate product-card selectors and, when none matched,
scanned all `a[href]`. A negative single-segment-path heuristic accepted ordinary VARUS pages.

The selected source of truth is server-rendered schema.org JSON-LD:

```text
ItemList.itemListElement[]
  ListItem.item
    @type = Product
    url
```

The parser also accepts a JSON-LD root array and `@graph`, but a URL is emitted only from a
Product nested in a ListItem nested in an ItemList. No CSS selector, blacklist, browser runtime,
or private API is used.

## Changes

- Removed approximate card selection, the all-anchor fallback, and path-based product guessing.
- Added JSON-LD parsing with safe handling of unrelated or malformed JSON-LD blocks.
- Preserved VARUS HTTPS/host validation, listing self-rejection, query/fragment removal, and
  case-insensitive deduplication.
- Preserved `listing_no_products_found` for HTTP 200 pages without verified products.
- Reworded the zero-product warning to name the expected JSON-LD source.
- Added a Debug diagnostic capped at ten verified URL samples.
- Replaced extractor fixtures with realistic Product ItemList data and added regressions for
  navigation/service anchors, generic-anchor fallback absence, duplicate/relative URLs,
  normalization, listing self-links, HTTP, JavaScript, malformed/cross-host candidates.
- Kept queue/progress semantics and labels unchanged.
- Removed the user-confirmed temporary `!!!` warning and preserved `.doc/Screenshot_4.png`.

## Live before/after verification

Target: `https://varus.ua/lactfree-food~brand_yagotinske`, HTTP 200 initial HTML on 2026-07-15.

```text
Legacy single-segment positive heuristic: 524 unique candidates
Verified JSON-LD Product ItemList:       6 product URLs
```

Legacy false samples included `/buyers`, `/giftcards`, `/help`, `/loyalty`, `/ordering`,
`/own-tm`, `/promotion`, `/stores`, and `/work`. The six after-results are listed in the
investigation report.

A Worker crawl was not started: the CLI has no single-listing, read-only command and normal Worker
startup would bootstrap/use the configured PostgreSQL database and could expand into queue work.
Instead, the bounded verification downloaded one target page without DB writes and inspected the
same initial-HTML Product ItemList contract used by the implementation. This leaves no database or
stage/production exposure, but it does not exercise Worker host startup.

## Graph and source validation

- Graphify associated the extractor with category-seed discovery and queue listing processing.
- Source confirmed DI registrations for `ICategoryProductLinkExtractor` and `IListingPageExtractor`.
- Source confirmed `ProductLinksDiscoveredFromListings` sums each listing result before queue
  idempotency; `ProductLinksEnqueuedFromListings` uses actual accepted count.
- CRG post-update indexed 62 nodes/184 edges from five C#/PowerShell files and reported no affected
  runtime flow. Its heuristic reported private parser methods as untested, but the public extractor
  path is covered by focused tests; build and full solution tests are authoritative.
- No SQL, EF, configuration, retry, locking, cancellation, concurrency, or DI changes occurred.
- Graphify was not refreshed after implementation because no structural relationship changed.

## Validation

```text
dotnet test PriceCrawler.Web.Tests\PriceCrawler.Web.Tests.csproj
  --filter "FullyQualifiedName~VarusListingPageExtractorTests|
            FullyQualifiedName~ProductUrlDiscoveryTests|
            FullyQualifiedName~PriceCollectionQueueProcessorProgressTests" --no-restore
Passed: 44, Failed: 0

dotnet restore PriceCrawler.sln
Succeeded; all projects up to date.

dotnet build PriceCrawler.sln
Succeeded; 0 warnings, 0 errors.

dotnet test PriceCrawler.sln --no-build
PriceCrawler.Web.Tests: 228 passed
PriceCrawler.Worker.Tests: 21 passed
Total: 249 passed, 0 failed, 0 skipped.

dotnet build PriceCrawler.sln -c Release --no-restore
Succeeded; 0 warnings, 0 errors.

git diff --check
Succeeded (line-ending conversion notices only).
```

Docker/database validation was not required because no persistence code or schema changed.

## Documentation

- Added ticket mirror under `Tickets/`.
- Added investigation and implementation reports under `Review/`.
- Added root Level-2 investigation and implementation-plan artifacts.
- Updated `CHANGELOG.md` because discovery/progress behavior is user-visible.
- README/Status/SQL documentation did not require changes; no CLI, schema, configuration, or
  operational workflow changed.

## Dashboard labels

Not changed. `Product URL найдено` continues to mean the sum of verified links found per listing
(not global unique URLs). `Product URL добавлено` remains the count accepted by queue enqueue after
idempotency. Fixing the input makes both metrics credible without a terminology change.

## Remaining risks

- VARUS may change or remove Product ItemList JSON-LD; the safe failure mode is zero links plus
  `listing_no_products_found`, not general-anchor fallback.
- No Worker-host smoke run was performed for the database-safety reason above.
- Malformed unrelated JSON-LD is intentionally ignored; absence of any valid Product ItemList is
  surfaced through the established listing diagnostic.

