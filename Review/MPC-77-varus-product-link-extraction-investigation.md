# MPC-77 VARUS Product-Link Extraction Investigation

## Context

- Date: 2026-07-15 (Europe/Kyiv).
- URL: `https://varus.ua/lactfree-food~brand_yagotinske`.
- Evidence inspected: browser-rendered DOM and a separate HTTP 200 initial-HTML response.
- Initial HTML size at inspection: 776,475 bytes.

## Findings

The rendered page contained 619 anchors. The old fallback accepted 524 unique HTTPS VARUS
single-segment paths in a bounded reproduction, including `/buyers`, `/promotion`, `/loyalty`,
`/help`, `/giftcards`, `/stores`, `/own-tm`, `/work`, and `/ordering`.

The initial HTML contains four JSON-LD scripts. The product source is the script whose root is:

```text
@type = ItemList
itemListElement[]
  @type = ListItem
  item.@type = Product
  item.sku
  item.url
```

On the investigated page this source contained exactly six product records. It is already
present in the server response; client-side rendering and a catalog API are not required.
The rendered DOM independently corroborated every URL through
`.sf-product-card a.sf-product-card__link`, but that CSS structure was not selected as the
parser contract because the structured product records provide a more explicit semantic signal.

Confirmed product URLs:

1. `https://varus.ua/moloko-yagotinske-bezlaktozne-25-750-g`
2. `https://varus.ua/moloko-bezlaktoznoe-25-yagotinske-950g`
3. `https://varus.ua/moloko-yagotinske-bezlaktoznoe-3-2-950-g`
4. `https://varus.ua/vershki-yagotinske-bezlaktozni-10-500-g`
5. `https://varus.ua/kasha-yagotinske-bezlaktozna-molochno-vivsyana-banan-2-500-g`
6. `https://varus.ua/jogurt-yagotinske-bezlaktoznij-15-750-g`

## Approaches considered

- Exact rendered card selector: corroborated, but more coupled to Vue/Storefront CSS classes.
- Embedded application state: not needed after finding semantic JSON-LD.
- JSON-LD: selected; present in initial HTML and explicitly distinguishes Product objects.
- Catalog/search API: not required and would introduce a private endpoint dependency.
- All-anchor scan plus blacklist: rejected; negative classification caused the defect and cannot
  reliably distinguish future ordinary single-segment pages.

## Semantics and impact

`ProductLinksDiscoveredFromListings` (worker label `Product URL найдено`) is the sum of verified
links returned by each processed listing. The same product may be counted again when found on
another listing. `ProductLinksEnqueuedFromListings` (`Product URL добавлено`) counts URLs actually
accepted by queue enqueue after idempotency/deduplication. The counters are internally coherent;
no UI label was changed because the current wording remains accurate after removing false links.

There is no database/schema, transaction, locking, retry, concurrency, request-rate, or
environment-safety change. Queue deduplication remains a secondary global guard.

## Known limitations

- Schema drift that removes or changes Product ItemList records yields zero products and the
  existing non-transient `listing_no_products_found` diagnostic.
- A truly empty listing and absent/malformed Product ItemList both yield zero; the warning names
  the missing verified source, while operators can inspect the live page to distinguish causes.
- Product query parameters are currently not identity-bearing in the observed records, so query
  and fragment removal remains appropriate.

## Repository intelligence

Graphify identified both consumers of `CategoryProductLinkExtractor`: category-seed catalog
discovery and `VarusListingPageExtractor` used by queue processing. CRG confirmed the changed
extractor and listing path and identified the focused extractor/discovery/progress tests. Source
inspection confirmed DI registrations and that progress increments before queue-level dedup.

