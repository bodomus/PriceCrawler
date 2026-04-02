# MPC-31 / Stage 2 handover

Historical note: this handover is now superseded by Stage 3 (`MPC-30`) manual live refresh work.
Write-side persistence was also later moved to PostgreSQL DB routines under `MPC-11`; use current architecture docs as source of truth.

## Summary

- `Price Chart` is no longer a placeholder on the `Runs` dashboard.
- The chart now loads from a dedicated read-only `ProductAnalytics` endpoint backed only by Postgres.
- Extended analytics are shown next to the chart:
  - selected price vs previous snapshot,
  - selected price vs first observed price,
  - observed min/max range and average price,
  - promo coverage and in-stock coverage across history.
- Existing `date -> run -> snapshot` navigation remains unchanged.
- Existing `ProductHistory` Kendo grid remains paged and is not reused as the analytics source.
- Fallback behavior from Stage 1 stays intact:
  - no live VARUS request on snapshot selection,
  - missing `brand/category/image` still degrade gracefully,
  - the old `ProductsGrid` endpoint still exists but is not used by this screen.

## Next Step Note

Target next step: `MPC-30` manual live VARUS refresh by explicit user action.

Recommended continuation:

1. Add a visible action on the `Product Card` such as `Refresh from VARUS`.
2. Keep the default dashboard flow read-only; live refresh must happen only on explicit click.
3. Return live payload separately from Postgres analytics so the user can compare:
   - current Postgres snapshot,
   - live VARUS response,
   - reconciliation status and any detected differences.
4. Decide how to persist results before implementation:
   - view-only comparison,
   - optional insert of a new snapshot,
   - or both with explicit confirmation.
5. Preserve current navigation semantics:
   - selecting a snapshot must not silently replace the selection with fresh live data,
   - any live refresh result should be visually isolated until the user chooses what to do next.
