# MPC-72 — Listing pages vs product pages

## Summary

Implemented explicit handling for Varus listing/filter URLs in the price collection queue.

## Changes

- Added `QueueItemKind` and persisted `price_collect_queue.page_kind`.
- Added Varus URL classification for listing/filter pages such as `~brand_`.
- Added `IListingPageExtractor` and `VarusListingPageExtractor`.
- Routed `ListingPage`/`CategoryPage` queue items to the listing extractor instead of the product extractor.
- Listing pages now discover, normalize, deduplicate, and enqueue product URLs as `ProductPage` queue items.
- Listing queue items are marked succeeded after successful listing parsing.
- Added explicit crawler error codes:
  - `listing_page_sent_to_product_extractor`
  - `listing_no_products_found`
  - `listing_parsed`
  - `product_links_discovered`
  - `unsupported_page_type`
- Added a guard in `VarusProductCardExtractor` so listing/filter URLs do not return generic `parse_failed`.
- Updated SQL bootstrap/routines and README documentation.

## Validation

- `dotnet build` passed.
- Targeted tests passed:
  - `VarusListingPageExtractorTests`
  - `VarusProductCardExtractorResiliencyTests`
  - `ProductUrlDiscoveryTests`
  - `CollectProductPricesUseCaseTests`
  - `RunCrawlerUseCaseTests`
- Full test suite passed:
  - `PriceCrawler.Worker.Tests`: 21 passed
  - `PriceCrawler.Web.Tests`: 209 passed

## Database

Tests and integration checks used `varprice_test` through `PostgresIntegrationFixture`.

No manual changes were made to the working `varprice` database.

## Notes

Existing uncommitted change in `docs/prompt_codex.txt` was present before implementation and was not part of this task.
