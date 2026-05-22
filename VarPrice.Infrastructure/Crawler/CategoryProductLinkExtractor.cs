using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace VarPrice.Infrastructure.Crawler;

public sealed class CategoryProductLinkExtractor : ICategoryProductLinkExtractor
{
    private static readonly Uri VarusBaseUri = new("https://varus.ua/");

    public IReadOnlyCollection<Uri> ExtractProductUrls(string html, Uri categoryUrl)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var anchor in SelectProductAnchors(document))
        {
            var href = anchor.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href) ||
                !Uri.TryCreate(VarusBaseUri, href.Trim(), out var uri) ||
                !VarusUrlRules.IsVarusHttpsUrl(uri) ||
                !LooksLikeProductUrl(uri, categoryUrl))
            {
                continue;
            }

            urls.Add(NormalizeProductCandidateUrl(uri).AbsoluteUri);
        }

        return urls.Select(x => new Uri(x)).ToList();
    }

    private static IEnumerable<IElement> SelectProductAnchors(IDocument document)
    {
        var cardAnchors = document.QuerySelectorAll(
            "[class*='product-card' i] a[href], [class*='product_tile' i] a[href], [data-testid*='product' i] a[href]");
        if (cardAnchors.Length > 0)
        {
            return cardAnchors;
        }

        return document.QuerySelectorAll("a[href]");
    }

    private static bool LooksLikeProductUrl(Uri uri, Uri categoryUrl)
    {
        var path = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (string.Equals(uri.AbsolutePath, categoryUrl.AbsolutePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (path.Contains('/'))
        {
            return false;
        }

        if (path.StartsWith("brands/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("blog", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("checkout", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("customer", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("img/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("media/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static Uri NormalizeProductCandidateUrl(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
            Query = string.Empty
        };

        return builder.Uri;
    }
}
