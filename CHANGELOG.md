# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## [Unreleased]
### Added
- `crawl_error` table and domain models for normalized crawler error persistence.
- Documentation for the normalized `product` / `price_snapshot` schema introduced by `MPC-21`.
- `ProductAnalytics` payload and real dashboard chart backed by Postgres history.
- Manual `RefreshLiveProduct` dashboard action for explicit live VARUS checks without automatic DB writes.

### Changed
- Database schema refactored around internal `product.id` links instead of legacy `product_key`.
- `price_snapshot` now stores `price` / `old_price` and acts as the fact table for product observations.
- Queue/pipeline, repositories, parser output, dashboard queries, and tests were aligned with the new storage model.
- `/Runs` dashboard now combines Postgres analytics with an explicit live comparison action on the product card.

### Fixed
- Removed stale documentation assumptions about `city`, `product_errors`, `discount_percent`, and `last_seen_at`.
