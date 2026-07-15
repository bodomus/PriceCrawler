# MPC-77 Implementation Plan

1. Replace approximate DOM selection and all-anchor fallback with JSON-LD Product ItemList parsing.
2. Preserve VARUS HTTPS validation, listing self-rejection, query/fragment removal, and
   case-insensitive deduplication.
3. Keep `ListingNoProductsFound`; add bounded success samples and a structure-specific warning.
4. Replace extractor fixtures with realistic JSON-LD and cover false/general/invalid links.
5. Validate category discovery and queue progress semantics without changing their counters.
6. Run live before/after verification, focused tests, build, solution tests, release build,
   post-change CRG, and documentation checks.

