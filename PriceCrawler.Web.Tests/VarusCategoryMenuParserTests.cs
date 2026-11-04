using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PriceCrawler.Application.Models;
using PriceCrawler.Domain.Categories;
using PriceCrawler.Infrastructure.Crawler;

namespace PriceCrawler.Web.Tests;

/// <summary>Tests for the varus.ua mega-menu parser and the tree source built on it.</summary>
public sealed class VarusCategoryMenuParserTests
{
    private static readonly Uri PageUrl = new("https://varus.ua/banani");

    // Mirrors the real markup: nested li/a/div[role=menu]/ul, an "open the whole section" self-link,
    // a brand facet, a category listed under two parents, three levels, a multi-segment path,
    // an image inside the link and a query string.
    private const string MenuHtml = """
        <html><body>
        <nav>
          <ul class="m-megamenu__main-list">
            <li class="a-megamenu-item">
              <a class="a-megamenu-item__link" href="/molochni-produkti-ta-yajtsya">
                <img src="/icons/milk.svg" alt="">
                <span>Молочні   продукти
                  та яйця</span>
              </a>
              <div role="menu"><ul>
                <li class="a-megamenu-item">
                  <a class="a-megamenu-item__link" href="/molochni-produkti-ta-yajtsya">Відкрити весь розділ</a>
                </li>
                <li class="a-megamenu-item">
                  <a class="a-megamenu-item__link" href="/moloko?utm=menu#top">Молоко</a>
                  <div role="menu"><ul>
                    <li class="a-megamenu-item"><a class="a-megamenu-item__link" href="/moloko-korovyache">Коров'яче</a></li>
                    <li class="a-megamenu-item"><a class="a-megamenu-item__link" href="/moloko~brand_galichina">Галичина</a></li>
                  </ul></div>
                </li>
                <li class="a-megamenu-item"><a class="a-megamenu-item__link" href="https://varus.ua/syr">Сир</a></li>
              </ul></div>
            </li>
            <li class="a-megamenu-item">
              <a class="a-megamenu-item__link" href="/varuscafe">VARUS cafe</a>
              <div role="menu"><ul>
                <li class="a-megamenu-item"><a class="a-megamenu-item__link" href="/varuscafe/pica">Піца</a></li>
                <li class="a-megamenu-item"><a class="a-megamenu-item__link" href="/syr">Сир</a></li>
                <li class="a-megamenu-item"><span>Без посилання</span></li>
              </ul></div>
            </li>
          </ul>
        </nav>
        </body></html>
        """;

    [Fact]
    public void Parse_ReadsNestedMenuInSiteOrder()
    {
        var categories = new VarusCategoryMenuParser().Parse(MenuHtml, PageUrl);

        Assert.Equal(
            [
                ("molochni-produkti-ta-yajtsya", null, 0),
                ("moloko", "molochni-produkti-ta-yajtsya", 0),
                ("moloko-korovyache", "moloko", 0),
                ("syr", "molochni-produkti-ta-yajtsya", 1),
                ("varuscafe", null, 1),
                ("varuscafe/pica", "varuscafe", 0),
                ("syr", "varuscafe", 1)
            ],
            categories.Select(c => (c.ExternalId, c.ParentExternalId, c.SortOrder)).ToArray());
    }

    [Fact]
    public void Parse_NormalizesNamesAndUrls()
    {
        var categories = new VarusCategoryMenuParser().Parse(MenuHtml, PageUrl);

        var root = categories[0];
        Assert.Equal("Молочні продукти та яйця", root.Name);
        Assert.Equal("https://varus.ua/molochni-produkti-ta-yajtsya", root.Url);
        Assert.Equal("https://varus.ua/moloko", categories.Single(c => c.ExternalId == "moloko").Url);
    }

    [Fact]
    public void Parse_TreeSnapshotKeepsFirstDuplicate()
    {
        var categories = new VarusCategoryMenuParser().Parse(MenuHtml, PageUrl);
        var tree = CategoryTreeSnapshot.Create(VarusCategoryTreeSource.Source, categories);

        Assert.Equal(6, tree.Nodes.Count);
        Assert.Equal(1, tree.DuplicateCount);
        Assert.Equal("molochni-produkti-ta-yajtsya", tree.Nodes.Single(n => n.ExternalId == "syr").ParentExternalId);
        Assert.Equal(
            ["moloko-korovyache", "syr", "varuscafe/pica"],
            tree.Leaves.Select(n => n.ExternalId).Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><body><ul class=\"other\"><li class=\"a-megamenu-item\"><a class=\"a-megamenu-item__link\" href=\"/moloko\">Молоко</a></li></ul></body></html>")]
    public void Parse_ReturnsEmptyWithoutMenu(string html)
    {
        Assert.Empty(new VarusCategoryMenuParser().Parse(html, PageUrl));
    }

    [Fact]
    public async Task TreeSource_ReturnsTreeFromMenuPage()
    {
        var loader = new StubPageLoader(CategoryPageLoadResult.Ok(MenuHtml));
        var source = CreateSource(loader, minimumNodes: 3);

        var tree = await source.GetTreeAsync(CancellationToken.None);

        Assert.NotNull(tree);
        Assert.Equal("varus", tree.Source);
        Assert.Equal(6, tree.Nodes.Count);
        Assert.Equal(new Uri("https://varus.ua/"), loader.RequestedUrl);
    }

    [Fact]
    public async Task TreeSource_ReturnsNullWhenTreeIsTooSmall()
    {
        var source = CreateSource(new StubPageLoader(CategoryPageLoadResult.Ok(MenuHtml)), minimumNodes: 50);

        Assert.Null(await source.GetTreeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TreeSource_ReturnsNullWhenPageFails()
    {
        var source = CreateSource(new StubPageLoader(CategoryPageLoadResult.Failed("http-503")), minimumNodes: 1);

        Assert.Null(await source.GetTreeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TreeSource_RejectsNonVarusUrl()
    {
        var loader = new StubPageLoader(CategoryPageLoadResult.Ok(MenuHtml));
        var source = CreateSource(loader, minimumNodes: 1, treeUrl: "https://example.com/");

        Assert.Null(await source.GetTreeAsync(CancellationToken.None));
        Assert.Null(loader.RequestedUrl);
    }

    private static VarusCategoryTreeSource CreateSource(
        ICategoryPageLoader loader,
        int minimumNodes,
        string treeUrl = "https://varus.ua/")
        => new(
            loader,
            new VarusCategoryMenuParser(),
            Options.Create(new CrawlerOptions { CategoryTreeUrl = treeUrl, CategoryTreeMinimumNodes = minimumNodes }),
            NullLogger<VarusCategoryTreeSource>.Instance);

    /// <summary>Page loader that returns one canned result and remembers the requested URL.</summary>
    private sealed class StubPageLoader(CategoryPageLoadResult result) : ICategoryPageLoader
    {
        public Uri? RequestedUrl { get; private set; }

        public Task<CategoryPageLoadResult> LoadAsync(CategorySeedUrl seed, Uri pageUrl, CancellationToken ct)
        {
            RequestedUrl = pageUrl;
            return Task.FromResult(result);
        }
    }
}
