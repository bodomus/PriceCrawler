# MPC-64 - Product catalog soft deactivation

## Summary

Implemented safe soft deactivation for `product_catalog` rows that disappear from a full Varus catalog refresh. The lifecycle is now:

```text
discovered -> active -> missing during refresh -> grace period -> inactive -> discovered again -> reactivated
```

Inactive catalog rows are not physically deleted, so product history, price snapshots, queue links, and audit state remain available.

## Runtime flow

```text
crawler_run_start
-> refresh_session_start
-> active_count
-> discovery
-> catalog_upsert
-> safety_check
-> deactivate_missing
-> refresh_session_complete
-> crawler_run_finish
```

## Database changes

- Added table `product_catalog_refresh`.
- Added `product_catalog.last_seen_refresh_id`, `deactivated_at`, `reactivated_at`.
- Added FK `product_catalog.last_seen_refresh_id -> product_catalog_refresh(id)` with `on delete restrict`.
- Added `ix_product_catalog_last_seen_refresh_id`.
- Added unique partial index `ux_product_catalog_refresh_running_source`.
- Added routines:
  - `product_catalog_refresh_start`
  - `product_catalog_refresh_complete`
  - `product_catalog_refresh_fail`
  - `product_catalog_refresh_get_by_id`
  - `product_catalog_get_active_count`
  - `product_catalog_deactivate_missing`
- Updated `product_catalog_upsert_discovered` to accept refresh id, write `last_seen_refresh_id`, and count reactivated rows.

## Safety guards

- Grace period: `CatalogMissingGracePeriodDays`, normalized to at least `1`.
- Absolute accepted URL threshold: `CatalogMinimumExpectedUrls`, normalized to at least `1`.
- Previous active catalog ratio: `CatalogMinimumPreviousRatio`, invalid values fall back to `0.5`.
- Scoped filter protection: non-empty `VegetablesUrlContains` skips deactivation.
- Supported full discovery mode: only `CategorySeeds` with empty scoped filter.
- Concurrent refresh protection: only one running refresh per source.

## Reactivation behavior

When an inactive row is found again, the existing row is reused:

- `is_active = true`
- `deactivated_at = null`
- `reactivated_at = discoveredAt`
- `last_seen_refresh_id = currentRefreshId`
- `next_check_at = null`

## Validation

- `dotnet restore`: completed; `NU1900` warnings because NuGet vulnerability metadata was unavailable.
- `dotnet build --no-restore`: passed; same `NU1900` warnings.
- `dotnet test --no-build`: passed, 174 tests.
- All PostgreSQL integration tests ran against `varprice_test`.

## Risks and follow-up

- Scheduler is still not implemented.
- Cleanup of old refresh sessions is still not implemented.
- Grace period is global for all categories.
- Completeness of `Api` and `Sitemap` discovery is not confirmed, so they do not enable deactivation.
- Physical deletion of catalog products is intentionally not implemented.
