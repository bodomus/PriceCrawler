# Review MPC-76

## Summary
- Replaced ambiguous crawler progress mutations with explicit product/listing queue progress methods.
- Added separate snapshot counters for product queue totals, product terminal processing, listing terminal processing, and listing-discovered product URL metrics.
- Updated price queue processing so ProductPage updates only product counters and ListingPage/CategoryPage update only listing counters.
- Product queue total now grows from listing processing only by the actual enqueue result, while discovered product URLs are tracked separately.
- Updated console dashboard to render listing and product progress independently and to use ProductProcessed / ProductQueueTotal for the primary product completion percentage.
- Added focused tests for progress state and queue processor product/listing success, terminal failure, retry, and found-vs-enqueued URL behavior.

## Validation
- dotnet test PriceCrawler.Web.Tests\PriceCrawler.Web.Tests.csproj --filter "CrawlerProgressStateTests|PriceCollectionQueueProcessorProgressTests" - passed, 20 tests.
- dotnet build PriceCrawler.sln - passed, 0 warnings, 0 errors.
- dotnet test PriceCrawler.sln - passed, Worker.Tests 21 and Web.Tests 218.

## Fixes after review
- ListingPage and CategoryPage thrown-exception paths now increment only listing terminal counters.
- ProductPage thrown-exception paths now increment only product terminal counters.
- Retry paths after thrown exceptions do not increment terminal product/listing counters.
- Discovery failure no longer increments ProductFailed or any other queue terminal counter.
- Legacy compatibility counters are derived aliases on CrawlerProgressSnapshot and cannot diverge from ProductQueueTotal/ProductProcessed/ProductSucceeded/ProductFailed.
- Initial mixed enqueue accounting no longer relies on Take(enqueued); enqueue now returns actual accepted counts by queue item kind.
- Dashboard metric labels are Russian/consistent with the existing Worker UI.
- Added thrown-exception tests for product, listing, category, and retry paths.
- Added discovery-failure progress test and DB enqueue-result test for mixed accepted item kinds.

Validation after fixes:
- dotnet build PriceCrawler.sln - passed, 0 warnings, 0 errors.
- dotnet test PriceCrawler.Web.Tests\PriceCrawler.Web.Tests.csproj --filter "CrawlerProgressStateTests|PriceCollectionQueueProcessorProgressTests|RunCrawlerUseCaseTests" - passed, 35 tests.
- dotnet test PriceCrawler.sln - passed, Worker.Tests 21 and Web.Tests 226.

## Notes
- No database schema or production data was changed.
- Tests were run through the solution test projects only; no production crawler run was executed.
