using System.Text.Json;

using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace PriceCrawler.Infrastructure.Crawler;

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

        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json' i]"))
        {
            ExtractProductUrlsFromJsonLd(script, categoryUrl, urls);
        }

        return urls.Select(x => new Uri(x)).ToList();
    }

    private static void ExtractProductUrlsFromJsonLd(
        IElement script,
        Uri categoryUrl,
        ISet<string> urls)
    {
        var json = script.TextContent;
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            ExtractProductUrlsFromRoot(document.RootElement, categoryUrl, urls);
        }
        catch (JsonException)
        {
            // Ignore unrelated malformed JSON-LD blocks. Without a valid product ItemList,
            // the caller safely reports that the listing contains no verified products.
        }
    }

    private static void ExtractProductUrlsFromRoot(
        JsonElement root,
        Uri categoryUrl,
        ISet<string> urls)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                ExtractProductUrlsFromRoot(item, categoryUrl, urls);
            }

            return;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (root.TryGetProperty("@graph", out var graph))
        {
            ExtractProductUrlsFromRoot(graph, categoryUrl, urls);
        }

        if (!HasSchemaType(root, "ItemList") ||
            !root.TryGetProperty("itemListElement", out var itemList) ||
            itemList.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var listItem in itemList.EnumerateArray())
        {
            if (listItem.ValueKind != JsonValueKind.Object ||
                !HasSchemaType(listItem, "ListItem") ||
                !listItem.TryGetProperty("item", out var product) ||
                product.ValueKind != JsonValueKind.Object ||
                !HasSchemaType(product, "Product") ||
                !product.TryGetProperty("url", out var urlElement) ||
                urlElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = urlElement.GetString();
            if (string.IsNullOrWhiteSpace(value) ||
                !Uri.TryCreate(VarusBaseUri, value.Trim(), out var uri) ||
                !VarusUrlRules.IsVarusHttpsUrl(uri) ||
                string.Equals(uri.AbsolutePath, categoryUrl.AbsolutePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            urls.Add(NormalizeProductUrl(uri).AbsoluteUri);
        }
    }

    private static bool HasSchemaType(JsonElement element, string expectedType)
    {
        if (!element.TryGetProperty("@type", out var type))
        {
            return false;
        }

        return type.ValueKind switch
        {
            JsonValueKind.String => string.Equals(type.GetString(), expectedType, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => type.EnumerateArray().Any(value =>
                value.ValueKind == JsonValueKind.String &&
                string.Equals(value.GetString(), expectedType, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    private static Uri NormalizeProductUrl(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
            Query = string.Empty
        };

        return builder.Uri;
    }
}
