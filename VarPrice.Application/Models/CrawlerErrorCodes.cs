namespace VarPrice.Application.Models;

public static class CrawlerErrorCodes
{
    public const string NotFound = "not_found";
    public const string TooManyRequests = "too_many_requests";
    public const string Timeout = "timeout";
    public const string Http5xx = "http_5xx";
    public const string ParseFailed = "parse_failed";
    public const string Unknown = "unknown";
}
