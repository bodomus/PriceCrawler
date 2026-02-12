using AngleSharp;
using AngleSharp.Dom;
using VarPrice.Web.Crawler;

namespace VarPrice.Web.Tests;

public sealed class PageKindDetectorTests
{
    [Fact]
    public async Task Detect_ProductPage_WhenJsonLdContainsProduct()
    {
        var html = """
                   <html>
                     <head>
                       <script type="application/ld+json">
                       {"@context":"https://schema.org","@type":"Product","name":"Test"}
                       </script>
                     </head>
                     <body></body>
                   </html>
                   """;

        var doc = await LoadDocumentAsync(html);
        var detector = new PageKindDetector();

        var kind = detector.Detect(doc);

        Assert.Equal(UrlKind.ProductPage, kind);
    }

    [Fact]
    public async Task Detect_CategoryPage_WhenJsonLdContainsItemList()
    {
        var html = """
                   <html>
                     <head>
                       <script type="application/ld+json">
                       {"@context":"https://schema.org","@type":"ItemList","name":"Category"}
                       </script>
                     </head>
                     <body></body>
                   </html>
                   """;

        var doc = await LoadDocumentAsync(html);
        var detector = new PageKindDetector();

        var kind = detector.Detect(doc);

        Assert.Equal(UrlKind.CategoryPage, kind);
    }

    [Fact]
    public async Task Detect_Unknown_WhenNoJsonLd()
    {
        var html = "<html><head></head><body>No scripts</body></html>";

        var doc = await LoadDocumentAsync(html);
        var detector = new PageKindDetector();

        var kind = detector.Detect(doc);

        Assert.Equal(UrlKind.Unknown, kind);
    }

    private static async Task<IDocument> LoadDocumentAsync(string html)
    {
        var ctx = BrowsingContext.New(Configuration.Default);
        return await ctx.OpenAsync(req => req.Content(html));
    }
}
