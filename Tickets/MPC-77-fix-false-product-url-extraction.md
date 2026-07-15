# MPC-77 — Fix false product URL extraction from VARUS listing pages

Canonical ticket: https://bodomus.youtrack.cloud/issue/MPC-77

## Objective

Replace approximate product-card selection and the unsafe `a[href]` fallback with positive,
evidence-based product identification for VARUS listing/category/brand pages. Preserve URL
normalization, VARUS HTTPS validation, deduplication, safe zero-product behavior, queue
idempotency, and meaningful bounded diagnostics.

## Required evidence and behavior

- Investigate `https://varus.ua/lactfree-food~brand_yagotinske` before implementation.
- Confirm at least five genuine product URLs and document the product source.
- Do not accept navigation, information, promotion, career, category, or service links.
- HTML without the verified product structure must return zero product URLs and surface
  `ListingNoProductsFound` through the established listing flow.
- Do not use a path blacklist as the primary classifier.
- Add deterministic realistic fixtures for products, duplicates, relative URLs,
  query/fragment normalization, listing self-links, ordinary anchors, invalid schemes,
  cross-host URLs, and queue/progress behavior.
- Keep diagnostics bounded to at most ten sample product URLs and remove temporary `!!!` logs.
- Review, but do not casually change, the dashboard meanings of "Product URL найдено" and
  "Product URL добавлено".
- Run focused tests, solution build/test, post-change CRG analysis, and a bounded live check.

## Acceptance summary

The source of truth must positively identify product records; the all-anchor fallback must be
gone; the live target must produce a plausible verified result rather than roughly 521 generic
links; deterministic CI must not depend on `varus.ua`; investigation and implementation reports
must record exact evidence, commands, results, remaining risks, and Graphify/CRG findings.

