# MPC-57 Review

## Summary

Implemented resilient sitemap discovery and validation for the VarPrice crawler.

## Done

- Added sitemap candidate discovery from configured URL, `robots.txt`, and fallback URLs.
- Added response validation for HTTP status, content type, empty body, HTML instead of XML, invalid XML, and sitemap root element.
- Added explicit `SitemapLoadFailureKind` classification.
- Added controlled `SitemapUnavailable` failure path for crawler runs when no valid sitemap is found.
- Preserved existing sitemap recursion, URL extraction, gzip/br decoding, and URL filtering behavior.
- Registered sitemap discovery services in DI.
- Added unit coverage for robots parsing, fallback candidates, deduplication, failure classification, valid sitemap roots, fallback discovery, and controlled run failure.

## Validation

- `dotnet test VarPrice.Web.Tests\VarPrice.Web.Tests.csproj --filter "FullyQualifiedName~SitemapReaderTests|FullyQualifiedName~RunCrawlerUseCaseTests"` passed.
- `dotnet build` passed with 0 warnings and 0 errors.

## Notes

Full `dotnet test` was not run because the repository includes integration tests that require a local PostgreSQL setup.
