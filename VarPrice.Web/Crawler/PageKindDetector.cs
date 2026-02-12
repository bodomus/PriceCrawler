using AngleSharp.Dom;
using System.Text.Json;

namespace VarPrice.Web.Crawler;

public interface IPageKindDetector
{
    UrlKind Detect(IDocument document);
}

public sealed class PageKindDetector : IPageKindDetector
{
    public UrlKind Detect(IDocument document)
    {
        if (document is null)
            return UrlKind.Unknown;

        var scripts = document.QuerySelectorAll("script[type='application/ld+json']");
        if (scripts.Length == 0)
            return UrlKind.Unknown;

        var hasProduct = false;
        var hasCategory = false;

        foreach (var script in scripts)
        {
            var json = script.TextContent;
            if (string.IsNullOrWhiteSpace(json))
                continue;

            if (!TryDetectInJson(json, ref hasProduct, ref hasCategory))
                continue;

            if (hasProduct)
                return UrlKind.ProductPage;
        }

        if (hasCategory)
            return UrlKind.CategoryPage;

        return UrlKind.Unknown;
    }

    private static bool TryDetectInJson(string json, ref bool hasProduct, ref bool hasCategory)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            ScanElement(doc.RootElement, ref hasProduct, ref hasCategory);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static void ScanElement(JsonElement element, ref bool hasProduct, ref bool hasCategory)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.NameEquals("@type"))
                    {
                        ScanTypeValue(prop.Value, ref hasProduct, ref hasCategory);
                    }
                    else
                    {
                        ScanElement(prop.Value, ref hasProduct, ref hasCategory);
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ScanElement(item, ref hasProduct, ref hasCategory);
                }
                break;
            case JsonValueKind.String:
                var value = element.GetString();
                if (IsCategoryType(value))
                    hasCategory = true;
                break;
        }
    }

    private static void ScanTypeValue(JsonElement value, ref bool hasProduct, ref bool hasCategory)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                var type = value.GetString();
                if (IsProductType(type))
                    hasProduct = true;
                if (IsCategoryType(type))
                    hasCategory = true;
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var itemType = item.GetString();
                        if (IsProductType(itemType))
                            hasProduct = true;
                        if (IsCategoryType(itemType))
                            hasCategory = true;
                    }
                    else
                    {
                        ScanTypeValue(item, ref hasProduct, ref hasCategory);
                    }
                }
                break;
            case JsonValueKind.Object:
                ScanElement(value, ref hasProduct, ref hasCategory);
                break;
        }
    }

    private static bool IsProductType(string? value)
        => string.Equals(value, "Product", StringComparison.OrdinalIgnoreCase);

    private static bool IsCategoryType(string? value)
        => string.Equals(value, "ItemList", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "CollectionPage", StringComparison.OrdinalIgnoreCase);
}
