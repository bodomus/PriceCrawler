# Codex Task: Separate Listing Pages From Product Pages in Varus Crawler

## Context

During the Varus crawl, some URLs return HTTP 200 but fail with `parse_failed` because they are not product pages. They are listing/filter/brand pages that contain product cards.

Observed log example:

```text
Extractor result https://varus.ua/kovbasi~brand_espana error_code=parse_failed http_status=200 latency_ms=6508 current_rps=0.49
[2026-07-01 09:35:39.030 +03:00 WRN] [ExecutionId=b85c5484794344b7a46db4b695819197] Queue item failed run_id=70 queue_id=19325 url=https://varus.ua/kovbasi~brand_espana error_code=parse_failed http_status=200 transient=false

Start processing HTTP request GET https://varus.ua/kovbasi~brand_gremio-de-la-carne
[2026-07-01 09:35:39.180 +03:00 INF] [ExecutionId=b85c5484794344b7a46db4b695819197] Sending HTTP request GET https://varus.ua/kovbasi~brand_gremio-de-la-carne
```

Examples:

```text
https://varus.ua/kovbasi~brand_espana
https://varus.ua/kovbasi~brand_gremio-de-la-carne
```

These URLs are category/listing/filter pages, not concrete product pages. One page may contain 6 products, another page may contain 1 product, but both must still be treated as listing pages.

## Problem

The current pipeline appears to send listing/filter pages into the product extractor. The product extractor expects a single product page and therefore fails parsing. This produces noisy `parse_failed` errors even though the HTTP request succeeded and the page is valid.

This is not a transient HTTP problem. It is a queue item classification / extractor routing problem.

## Goal

Introduce explicit page type handling so that:

1. Product pages are parsed by the product extractor.
2. Listing/category/filter/brand pages are parsed by a listing extractor.
3. Listing pages produce discovered product URLs.
4. Discovered product URLs are enqueued as product queue items.
5. Listing pages are not recorded as failed product parses.

## Required Behavior

### 1. Add page kind classification

Add or extend a queue item page kind concept.

Suggested enum:

```csharp
public enum QueueItemKind
{
    Unknown = 0,
    ProductPage = 1,
    ListingPage = 2,
    CategoryPage = 3,
    SitemapPage = 4,
    ApiPage = 5
}
```

Use the project’s existing naming/style if there is already a similar enum or status model.

### 2. Detect Varus listing/filter URLs

URLs containing filter markers like `~brand_` must not be routed to the product extractor.

Examples that should be classified as listing pages:

```text
https://varus.ua/kovbasi~brand_espana
https://varus.ua/kovbasi~brand_gremio-de-la-carne
```

Initial URL-level heuristic is acceptable:

```csharp
private static bool LooksLikeVarusListingUrl(Uri uri)
{
    var path = uri.AbsolutePath;

    return path.Contains("~", StringComparison.OrdinalIgnoreCase)
        || path.Contains("~brand_", StringComparison.OrdinalIgnoreCase);
}
```

Prefer a cleaner project-level abstraction if one already exists.

Important: do not rely only on `FoundCount == 1`. A listing page with one product is still a listing page.

### 3. Add listing extraction flow

For a listing page, the extractor must:

1. Fetch the page.
2. Parse product cards / product links from the listing HTML.
3. Normalize product URLs to absolute Varus URLs.
4. Deduplicate links.
5. Enqueue discovered product URLs as product page queue items.
6. Mark the listing queue item as processed/discovered, not as product parse failure.

Suggested result model:

```csharp
public sealed class ListingExtractionResult
{
    public required string SourceUrl { get; init; }
    public IReadOnlyList<string> ProductUrls { get; init; } = Array.Empty<string>();
    public int FoundCount => ProductUrls.Count;
}
```

Adapt this to the existing project conventions.

### 4. Add safer error codes / statuses

Do not use generic `parse_failed` for valid listing pages sent to the wrong extractor.

Add or reuse explicit statuses/error codes similar to:

```text
unsupported_page_type
listing_page_sent_to_product_extractor
listing_parsed
listing_no_products_found
product_links_discovered
```

Expected behavior:

- If a product extractor receives a listing page, it should return `unsupported_page_type` or `listing_page_sent_to_product_extractor`, not generic `parse_failed`.
- If a listing page contains product links, it should be marked successful.
- If a listing page contains no product links, it should be marked as `listing_no_products_found`, not transient failure.

### 5. Preserve HTTP/transient semantics

HTTP status handling must remain separate from parsing/page classification.

- `http_status=200` plus wrong parser should not be transient.
- HTTP timeouts, 429, 5xx, connection errors may remain transient depending on existing rules.
- Valid listing page with zero products is not an HTTP failure.

## Acceptance Criteria

### AC1: Brand listing pages are not processed as product pages

Given this URL:

```text
https://varus.ua/kovbasi~brand_espana
```

When the crawler processes it,
then it must be routed to the listing extractor, not the product extractor.

### AC2: Listing pages discover product URLs

Given a listing page that contains 6 product cards,
when the listing extractor processes it,
then it must discover and enqueue 6 normalized product URLs, minus duplicates.

### AC3: Listing page with one product is still listing page

Given this URL:

```text
https://varus.ua/kovbasi~brand_gremio-de-la-carne
```

when the page contains one product card,
then it must still be treated as a listing page and must enqueue the single discovered product URL.

### AC4: No false `parse_failed` for valid listing pages

Given a listing/filter/brand page returns HTTP 200,
when it is processed successfully as a listing page,
then the queue item must not be marked as `parse_failed`.

### AC5: Wrong extractor guard exists

Given a listing page accidentally reaches the product extractor,
then the product extractor must fail with an explicit non-transient status such as:

```text
listing_page_sent_to_product_extractor
```

or

```text
unsupported_page_type
```

not generic `parse_failed`.

### AC6: Existing product page extraction remains unchanged

Given a normal product page URL,
when the crawler processes it,
then existing product extraction behavior must continue to work.

No regression in product snapshot creation is allowed.

## Implementation Notes

Recommended architecture:

```text
Queue item
  -> classify page kind
  -> route to extractor
      ProductPage  -> ProductExtractor
      ListingPage  -> ListingExtractor
      SitemapPage  -> Sitemap/API discovery logic if applicable
```

Avoid hardcoding this logic deep inside the product parser. Classification/routing should happen before extractor execution where possible.

Suggested interface shape if useful:

```csharp
public interface IPageExtractor<TExtractionResult>
{
    Task<TExtractionResult> ExtractAsync(string url, string html, CancellationToken cancellationToken);
}
```

or adapt to current project interfaces.

## Logging Requirements

Add structured logs that make the routing visible.

Expected log fields:

```text
run_id
queue_id
url
page_kind
extractor
http_status
found_product_links
error_code
transient
```

Example successful listing log:

```text
Listing page parsed run_id=70 queue_id=19325 url=https://varus.ua/kovbasi~brand_espana page_kind=ListingPage found_product_links=6
```

Example wrong-extractor guard log:

```text
Unsupported page type for product extractor run_id=70 queue_id=19325 url=https://varus.ua/kovbasi~brand_espana error_code=listing_page_sent_to_product_extractor transient=false
```

## Tests Required

Add unit tests or integration tests according to current test structure.

Required cases:

1. URL classifier recognizes `~brand_` pages as listing pages.
2. Listing extractor extracts multiple product links from sample listing HTML.
3. Listing extractor extracts one product link from sample listing HTML.
4. Listing extractor deduplicates product links.
5. Product extractor guard rejects listing pages with explicit non-transient status.
6. Normal product page extraction still passes existing tests.

Use saved/minimal HTML fixtures if the project already has fixture-based parser tests.

## Non-Goals

Do not implement full anti-ban behavior in this task.

Do not redesign the whole crawler queue.

Do not change unrelated database schema unless the existing schema cannot represent page kind or explicit error/status codes.

Do not tune concurrency/RPS in this task.

## Definition of Done

The task is complete when:

1. Listing/filter/brand pages are classified separately from product pages.
2. Listing pages extract and enqueue product URLs.
3. Valid listing pages no longer produce noisy product `parse_failed` results.
4. Explicit error/status codes exist for unsupported page type or wrong extractor routing.
5. Tests cover classification, listing extraction, deduplication, and product extraction regression.
6. Logs clearly show page kind, extractor, discovered product count, and error code where applicable.
