namespace VarPrice.Application.Models;

public static class CrawlerErrorCodes
{
    public const string NotFound = "not_found";
    public const string TooManyRequests = "too_many_requests";
    public const string Timeout = "timeout";
    public const string Http5xx = "http_5xx";
    public const string ParseFailed = "parse_failed";
    public const string ListingParsed = "listing_parsed";
    public const string ListingNoProductsFound = "listing_no_products_found";
    public const string ProductLinksDiscovered = "product_links_discovered";
    public const string ListingPageSentToProductExtractor = "listing_page_sent_to_product_extractor";
    public const string UnsupportedPageType = "unsupported_page_type";
    public const string ProductUrlDiscoveryUnavailable = "ProductUrlDiscoveryUnavailable";
    public const string Unknown = "unknown";
}
