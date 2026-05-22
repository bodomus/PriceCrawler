namespace VarPrice.Infrastructure.Crawler;

public interface ICategoryProductLinkExtractor
{
    IReadOnlyCollection<Uri> ExtractProductUrls(string html, Uri categoryUrl);
}
