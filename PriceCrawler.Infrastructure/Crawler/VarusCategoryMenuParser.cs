using AngleSharp.Dom;
using AngleSharp.Html.Parser;

using PriceCrawler.Domain.Categories;

namespace PriceCrawler.Infrastructure.Crawler;

/// <summary>
/// Parses the varus.ua mega menu (<c>ul.m-megamenu__main-list</c>), which every server-rendered page
/// carries in full. A menu item is <c>li.a-megamenu-item</c> with a direct <c>a.a-megamenu-item__link</c>;
/// its sub-items are the nested menu items whose closest enclosing item is this one.
/// Filter links (<c>/moloko~brand_galichina</c>) and "open the whole section" links back to the parent
/// are not categories and are skipped.
/// </summary>
public sealed class VarusCategoryMenuParser : IVarusCategoryMenuParser
{
    private const string MenuItemSelector = "li.a-megamenu-item";

    /// <inheritdoc />
    public IReadOnlyList<DiscoveredCategory> Parse(string html, Uri pageUrl)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        var document = new HtmlParser().ParseDocument(html);
        var menu = document.QuerySelector("ul.m-megamenu__main-list");
        if (menu is null)
        {
            return [];
        }

        var categories = new List<DiscoveredCategory>();
        var topLevel = menu.Children.Where(IsMenuItem).ToList();
        AddLevel(topLevel, parentKey: null, pageUrl, categories);
        return categories;
    }

    /// <summary>Adds one menu level depth-first, so every parent precedes its children.</summary>
    private static void AddLevel(
        IReadOnlyList<IElement> items,
        string? parentKey,
        Uri pageUrl,
        List<DiscoveredCategory> categories)
    {
        var sortOrder = 0;
        foreach (var item in items)
        {
            var link = item.Children.FirstOrDefault(child =>
                child.LocalName == "a" && child.ClassList.Contains("a-megamenu-item__link"));
            var href = link?.GetAttribute("href");
            if (link is null ||
                string.IsNullOrWhiteSpace(href) ||
                !Uri.TryCreate(pageUrl, href.Trim(), out var url) ||
                !VarusUrlRules.TryGetCategoryKey(url, out var key) ||
                key == parentKey)
            {
                continue;
            }

            var name = NormalizeWhitespace(link.TextContent);
            if (name.Length == 0)
            {
                continue;
            }

            categories.Add(new DiscoveredCategory(key, name, CanonicalUrl(url), parentKey, sortOrder++));
            var children = item.QuerySelectorAll(MenuItemSelector)
                .Where(child => ClosestMenuItem(child.ParentElement) == item)
                .ToList();
            AddLevel(children, key, pageUrl, categories);
        }
    }

    private static bool IsMenuItem(IElement element)
        => element.LocalName == "li" && element.ClassList.Contains("a-megamenu-item");

    private static IElement? ClosestMenuItem(IElement? element)
    {
        while (element is not null && !IsMenuItem(element))
        {
            element = element.ParentElement;
        }

        return element;
    }

    private static string CanonicalUrl(Uri url)
        => new UriBuilder(url) { Query = string.Empty, Fragment = string.Empty }.Uri.AbsoluteUri;

    private static string NormalizeWhitespace(string text)
        => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
