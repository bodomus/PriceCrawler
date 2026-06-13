# MPC-64 - Реализовать деактивацию товаров, исчезнувших из каталога Varus

## Summary

Добавлен безопасный soft-deactivation flow для `product_catalog`: полный `catalog-refresh` создает refresh session, помечает найденные URL текущим `last_seen_refresh_id`, проходит safety checks и только после этого деактивирует давно не найденные активные строки. История `product`, `price_snapshot`, queue items и сами строки каталога не удаляются.

## Implemented

- Added `product_catalog_refresh` table and repository.
- Added `product_catalog.last_seen_refresh_id`, `deactivated_at`, `reactivated_at`.
- Extended catalog upsert with `refreshId` and `ReactivatedCount`.
- Added automatic reactivation for inactive rows found again.
- Added `DeactivateMissingAsync` with grace period cutoff and active lease protection.
- Added safety policy for:
  - feature flag;
  - scoped filter;
  - full discovery mode;
  - minimum accepted URL count;
  - previous active ratio.
- Updated `RefreshProductCatalogUseCase` flow and result counters.
- Updated configuration and documentation.
- Added unit and PostgreSQL integration tests.

## Validation

- `dotnet restore`: completed with `NU1900` warnings because NuGet vulnerability metadata was unavailable.
- `dotnet build --no-restore`: passed with the same `NU1900` warnings.
- `dotnet test --no-build`: passed, 174 tests.
- PostgreSQL integration tests used only `varprice_test`.

## Notes

- Deactivation is enabled only for full `CategorySeeds` refresh with empty `VegetablesUrlContains`.
- `Api` and `Sitemap` discovery modes skip deactivation until their completeness is confirmed.
- Physical deletion is not implemented.
