λ graphify query "ProductUrlDiscoveryStrategyFactory IProductUrlDiscoveryStrategy CategorySeedProductUrlDiscoveryStrategy DiscoverProductUrlsAsync"
Traversal: BFS depth=2 | Start: ['CategorySeedProductUrlDiscoveryStrategy', 'IProductUrlDiscoveryStrategy', 'ProductUrlDiscoveryStrategyFactory', '.DiscoverProductUrlsAsync()'] | 102 nodes found

NODE PriceCrawler.Application.Models [src=PriceCrawler.Application/Models/CategorySeedUrlFileOptions.cs loc=L1 community=Price Crawler Application]
NODE PriceCrawler.Application.Abstractions [src=PriceCrawler.Application/Abstractions/ICategoryProductUrlDiscoverySource.cs loc=L1 community=Price Crawler Application] NODE ProductUrlDiscoveryTests [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L17 community=Product URL Discovery Tests]
NODE PriceCrawler.Infrastructure.Crawler [src=PriceCrawler.Infrastructure/Crawler/CategoryDiscoveryStopReasons.cs loc=L1 community=Price Crawler Application]
NODE PriceCrawler.Application.UseCases [src=PriceCrawler.Application/UseCases/ApiProductUrlDiscoveryStrategy.cs loc=L4 community=Price Crawler Domain]
NODE Fact [src= loc= community=Product URL Discovery Tests]
NODE Task [src= loc= community=Category Product URL Discovery]
NODE ProductUrlDiscoveryTests.cs [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L1 community=Price Crawler Application]
NODE .CreateCategorySource() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L561 community=Category Product URL Discovery]
NODE .DiscoverProductUrlsAsync() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L655 community=Category Product URL Discovery]
NODE .GetSnapshot() [src=PriceCrawler.Application/Models/CrawlerProgressState.cs loc=L170 community=Crawler Progress Formatter]
NODE CategorySeedProductUrlDiscoveryStrategy [src=PriceCrawler.Infrastructure/Crawler/CategoryProductUrlDiscoverySource.cs loc=L9 community=Category Product URL Discovery]
NODE .DiscoverSeedProductUrlsAsync() [src=PriceCrawler.Infrastructure/Crawler/CategoryProductUrlDiscoverySource.cs loc=L62 community=Category Product URL Discovery]
NODE IProductUrlDiscoveryStrategy [src=PriceCrawler.Application/Abstractions/IProductUrlDiscoveryStrategy.cs loc=L5 community=Product URL Discovery Strategy]
NODE .CategorySeedSource_ReportsDiscoveryProgressAfterEachPage() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L269 community=Category Product URL Discovery]
NODE .CreateTrackedHttpClient() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L624 community=Category Product URL Discovery]
NODE .CreateHttpClient() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L621 community=Category Product URL Discovery]
NODE .DiscoverProductUrlsAsync() [src=PriceCrawler.Application/UseCases/ProductUrlDiscoveryService.cs loc=L15 community=Product URL Discovery Strategy]
NODE .Html() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L632 community=Category Product URL Discovery]
NODE .WriteSeedFile() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L743 community=Category Product URL Discovery]
NODE .CategorySeedSource_FollowsNextPagesUntilNoNewProducts() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L233 community=Category Product URL Discovery]

NODE .CategorySeedSource_StopsOnNoNewProductUrls() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L303 community=Category Product URL Discovery]
NODE .CategorySeedSource_StopsOnNoNextPage() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L373 community=Category Product URL Discovery]
NODE .CategorySeedSource_DeduplicatesUrlsAcrossPages() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L393 community=Category Product URL Discovery]
NODE .CategorySeedSource_StopsOnMaxCategoryPagesPerSeed() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L338 community=Category Product URL Discovery]
NODE .CategorySeedSource_ValidatesSeeds_DeduplicatesAndExtractsProducts() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L134 community=Category Product URL Discovery]
NODE ProductUrlDiscoverySourceKind [src=PriceCrawler.Application/Models/ProductUrlDiscoveryResult.cs loc=L7 community=Crawler Run Result]
NODE CategorySeedUrl [src=PriceCrawler.Infrastructure/Crawler/CategorySeedUrl.cs loc=L3 community=Category Product URL Discovery]
NODE .CategorySeedSource_CategoryHttpErrors_ContinueWithOtherSeeds() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L204 community=Category Product URL Discovery]
NODE .CreateStrategyFactory() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L596 community=Product URL Discovery Tests]
NODE .DiscoverProductUrlsAsync() [src=PriceCrawler.Infrastructure/Crawler/CategoryProductUrlDiscoverySource.cs loc=L31 community=Category Product URL Discovery]
NODE .DiscoverProductUrlsAsync() [src=PriceCrawler.Infrastructure/Crawler/CategoryProductUrlDiscoverySource.cs loc=L172 community=Category Product URL Discovery]
NODE .DiscoverAsync() [src=PriceCrawler.Application/UseCases/SitemapProductUrlDiscoveryStrategy.cs loc=L18 community=Product URL Retrieval]
NODE .SeedJson() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L638 community=Category Product URL Discovery]
NODE .CategorySeedSource_InvalidJson_ThrowsClearError() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L177 community=Category Product URL Discovery]
NODE .LoadAsync() [src=PriceCrawler.Infrastructure/Crawler/ICategoryPageLoader.cs loc=L5 community=Category Page Loader]
NODE .CategorySeedSource_MissingFile_ThrowsClearError() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L191 community=Category Product URL Discovery]
NODE .GetSeedsAsync() [src=PriceCrawler.Infrastructure/Crawler/ICategorySeedProvider.cs loc=L5 community=Category Seed Provider]
NODE .DiscoverAsync() [src=PriceCrawler.Infrastructure/Crawler/CategoryProductUrlDiscoverySource.cs loc=L23 community=Category Product URL Discovery]
NODE .DiscoverAsync() [src=PriceCrawler.Application/Abstractions/IProductUrlDiscoveryStrategy.cs loc=L11 community=API Product URL Discovery]
NODE ProductDiscoveryItem [src=PriceCrawler.Application/Models/ProductDiscoveryItem.cs loc=L3 community=API Product URL Discovery]
... (truncated — 61 more nodes cut by ~2000-token budget. Narrow with context_filter=['call'] or use get_node for a specific symbol)



λ graphify query "CategorySeedProductUrlDiscoveryStrategy DiscoverSeedProductUrlsAsync CategoryPageLoader CategoryPaginationStrategy CategoryProductLinkExtractor"
Traversal: BFS depth=2 | Start: ['CategorySeedProductUrlDiscoveryStrategy', 'CategoryPageLoader', 'CategoryPaginationStrategy', 'CategoryProductLinkExtractor', '.DiscoverSeedProductUrlsAsync()'] | 105 nodes found

NODE PriceCrawler.Application.Models [src=PriceCrawler.Application/Models/CategorySeedUrlFileOptions.cs loc=L1 community=Price Crawler Application]
NODE PriceCrawler.Application.Abstractions [src=PriceCrawler.Application/Abstractions/ICategoryProductUrlDiscoverySource.cs loc=L1 community=Price Crawler Application] NODE ProductUrlDiscoveryTests [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L17 community=Product URL Discovery Tests]
NODE PriceCrawler.Infrastructure.Crawler [src=PriceCrawler.Infrastructure/Crawler/CategoryDiscoveryStopReasons.cs loc=L1 community=Price Crawler Application]
NODE ProductUrlDiscoveryTests.cs [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L1 community=Price Crawler Application]
NODE .DiscoverProductUrlsAsync() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L655 community=Category Product URL Discovery]
NODE .DiscoverSeedProductUrlsAsync() [src=PriceCrawler.Infrastructure/Crawler/CategoryProductUrlDiscoverySource.cs loc=L62 community=Category Product URL Discovery]
NODE CategorySeedProductUrlDiscoveryStrategy [src=PriceCrawler.Infrastructure/Crawler/CategoryProductUrlDiscoverySource.cs loc=L9 community=Category Product URL Discovery]
NODE IProductUrlDiscoveryStrategy [src=PriceCrawler.Application/Abstractions/IProductUrlDiscoveryStrategy.cs loc=L5 community=Product URL Discovery Strategy]
NODE .LoadAsync() [src=PriceCrawler.Infrastructure/Crawler/CategoryPageLoader.cs loc=L12 community=Category Page Loader]
NODE .ExtractAsync() [src=PriceCrawler.Infrastructure/Crawler/VarusListingPageExtractor.cs loc=L17 community=Listing Page Extractor]
NODE ProductUrlDiscoverySourceKind [src=PriceCrawler.Application/Models/ProductUrlDiscoveryResult.cs loc=L7 community=Crawler Run Result]
NODE CategorySeedUrl [src=PriceCrawler.Infrastructure/Crawler/CategorySeedUrl.cs loc=L3 community=Category Product URL Discovery]
NODE .CreateStrategyFactory() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L596 community=Product URL Discovery Tests]
NODE .DiscoverProductUrlsAsync() [src=PriceCrawler.Infrastructure/Crawler/CategoryProductUrlDiscoverySource.cs loc=L31 community=Category Product URL Discovery]
NODE .LoadAsync() [src=PriceCrawler.Infrastructure/Crawler/ICategoryPageLoader.cs loc=L5 community=Category Page Loader]
NODE CategoryProductLinkExtractor [src=PriceCrawler.Infrastructure/Crawler/CategoryProductLinkExtractor.cs loc=L6 community=Category Product Link Extraction]
NODE .ExtractProductUrls() [src=PriceCrawler.Infrastructure/Crawler/CategoryProductLinkExtractor.cs loc=L10 community=Category Product Link Extraction]
NODE .GetSeedsAsync() [src=PriceCrawler.Infrastructure/Crawler/CategorySeedProvider.cs loc=L15 community=Category Seed Provider]
NODE .DiscoverProductUrlsAsync() [src=PriceCrawler.Infrastructure/Crawler/CategoryProductUrlDiscoverySource.cs loc=L172 community=Category Product URL Discovery]
NODE .AcquireRequestSlotAsync() [src=PriceCrawler.Infrastructure/Crawler/VarusRequestCoordinator.cs loc=L50 community=Request Coordinator]
NODE .GetSeedsAsync() [src=PriceCrawler.Infrastructure/Crawler/ICategorySeedProvider.cs loc=L5 community=Category Seed Provider]
NODE CategoryPaginationStrategy [src=PriceCrawler.Infrastructure/Crawler/CategoryPaginationStrategy.cs loc=L6 community=Category Pagination Strategy]
NODE .DiscoverAsync() [src=PriceCrawler.Application/Abstractions/IProductUrlDiscoveryStrategy.cs loc=L11 community=API Product URL Discovery]
NODE ProductDiscoveryItem [src=PriceCrawler.Application/Models/ProductDiscoveryItem.cs loc=L3 community=API Product URL Discovery]
NODE .DiscoverAsync() [src=PriceCrawler.Infrastructure/Crawler/CategoryProductUrlDiscoverySource.cs loc=L23 community=Category Product URL Discovery]
NODE CategoryProductUrlDiscoverySourceHarness [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L649 community=Extractor Harness]
NODE .GetNextPageUrl() [src=PriceCrawler.Infrastructure/Crawler/CategoryPaginationStrategy.cs loc=L8 community=Category Pagination Strategy]
NODE .IsVarusHttpsUrl() [src=PriceCrawler.Infrastructure/Crawler/VarusUrlRules.cs loc=L5 community=Category Pagination Strategy]
NODE ApiProductUrlDiscoveryStrategy [src=PriceCrawler.Application/UseCases/ApiProductUrlDiscoveryStrategy.cs loc=L6 community=API Product URL Discovery]
NODE CategoryProductUrlDiscoverySource.cs [src=PriceCrawler.Infrastructure/Crawler/CategoryProductUrlDiscoverySource.cs loc=L1 community=Price Crawler Application]
NODE .ExtractProductUrls() [src=PriceCrawler.Infrastructure/Crawler/ICategoryProductLinkExtractor.cs loc=L5 community=Category Product Link Extractor]
NODE CategoryPageLoadResult [src=PriceCrawler.Infrastructure/Crawler/CategoryPageLoadResult.cs loc=L3 community=Category Page Loader]
NODE .SelectProductAnchors() [src=PriceCrawler.Infrastructure/Crawler/CategoryProductLinkExtractor.cs loc=L38 community=Category Product Link Extraction]
NODE .CreateDiscoveryService() [src=PriceCrawler.Web.Tests/ProductUrlDiscoveryTests.cs loc=L549 community=Product URL Discovery Strategy]
NODE ICategoryProductUrlDiscoverySource [src=PriceCrawler.Application/Abstractions/ICategoryProductUrlDiscoverySource.cs loc=L3 community=Product URL Discovery]
NODE .Ok() [src=PriceCrawler.Infrastructure/Crawler/CategoryPageLoadResult.cs loc=L5 community=Crawler Run Repository]
NODE .LogCategoryPageProcessed() [src=PriceCrawler.Infrastructure/Crawler/CategoryProductUrlDiscoverySource.cs loc=L147 community=Category Product URL Discovery]
NODE Task [src= loc= community=Category Product URL Discovery]
NODE CategoryPageLoader [src=PriceCrawler.Infrastructure/Crawler/CategoryPageLoader.cs loc=L7 community=Category Page Loader]
NODE .GetNextPageUrl() [src=PriceCrawler.Infrastructure/Crawler/ICategoryPaginationStrategy.cs loc=L5 community=Category Pagination Strategy]
NODE CancellationToken [src= loc= community=Category Product URL Discovery]
... (truncated — 63 more nodes cut by ~2000-token budget. Narrow with context_filter=['call'] or use get_node for a specific symbol)



λ graphify path "ProductUrlDiscoveryStrategyFactory" "CategorySeedProductUrlDiscoveryStrategy"
Shortest path (2 hops):
  ProductUrlDiscoveryStrategyFactory <--references [EXTRACTED]-- .CreateStrategyFactory() --references [EXTRACTED]--> CategorySeedProductUrlDiscoveryStrategy
  
  
  λ graphify path "CategorySeedProductUrlDiscoveryStrategy" "CategoryPageLoader"
Shortest path (4 hops):
  CategorySeedProductUrlDiscoveryStrategy <--contains [EXTRACTED]-- CategoryProductUrlDiscoverySource.cs --contains [EXTRACTED]--> PriceCrawler.Infrastructure.Crawler <--contains [EXTRACTED]-- CategoryPageLoader.cs --contains [EXTRACTED]--> CategoryPageLoader