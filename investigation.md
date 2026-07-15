# MPC-77 Investigation

See `Review/MPC-77-varus-product-link-extraction-investigation.md` for the durable report.

- Workflow level: 2.
- Owning component: `PriceCrawler.Infrastructure/Crawler/CategoryProductLinkExtractor`.
- Consumers: category-seed discovery and queue listing/category processing.
- Root cause: fallback from approximate card selectors to every `a[href]`, followed by a
  single-path-segment heuristic.
- Selected source: server-rendered schema.org JSON-LD Product `ItemList`.
- Database/schema impact: none.
- Queue contract: unchanged; only positively verified URLs reach enqueue.

