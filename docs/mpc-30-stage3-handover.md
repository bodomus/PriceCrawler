# MPC-30 / Stage 3 handover

## Summary

- The `Runs` dashboard now supports a manual `Refresh from VARUS` action on the selected product card.
- The action is explicit-only:
  - selecting a snapshot does not trigger live HTTP requests;
  - the user must click the button to fetch current VARUS data.
- Live refresh reuses the existing extractor pipeline through `IProductCardExtractor`.
- The response is read-only:
  - it returns live card data,
  - extractor metadata (`status`, `httpStatus`, `latencyMs`, `approximateRps`),
  - and issue information when extraction is partial or failed.
- The UI compares live VARUS values against the current Postgres snapshot for:
  - name,
  - SKU,
  - current price,
  - old price,
  - promo flag,
  - stock flag,
  - unit,
  - slug.
- No live result is persisted automatically.

## Next Step Note

Recommended next step: decide and implement what should happen after a successful live comparison.

Suggested options to resolve before coding:

1. View-only mode:
   keep live refresh informational and do not allow persistence.
2. Confirm-and-save mode:
   let the user explicitly create a new `price_snapshot` from the live result after reviewing the diff.
3. Reconcile mode:
   persist a new snapshot only when tracked fields changed and the user confirms the write.

Important guardrails for the next stage:

- Do not auto-write on live fetch completion.
- Keep the current `date -> run -> snapshot` selection stable until the user chooses a follow-up action.
- If persistence is added, expose a clear success path:
  - what was written,
  - whether a new snapshot row was created,
  - how the dashboard selection should refresh after that.
