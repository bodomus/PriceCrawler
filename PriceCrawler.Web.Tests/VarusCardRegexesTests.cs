using PriceCrawler.Infrastructure.Crawler;

namespace PriceCrawler.Web.Tests;

/// <summary>Tests for the build-time generated regular expressions of the product card parser (MPC-109).</summary>
public sealed class VarusCardRegexesTests
{
    private const string Sqpp =
        """
        "sqpp":{"price": 89.90, "special_price":74.5,"special_price_discount": 17,
        "available":TRUE,"special_price_from_date":"2026-11-10"}
        """;

    [Fact]
    public void SqppRegexes_ReadEveryField()
    {
        Assert.Equal("89.90", VarusCardRegexes.Price().Match(Sqpp).Groups["v"].Value);
        Assert.Equal("74.5", VarusCardRegexes.SpecialPrice().Match(Sqpp).Groups["v"].Value);
        Assert.Equal("17", VarusCardRegexes.SpecialPriceDiscount().Match(Sqpp).Groups["v"].Value);
        Assert.Equal("TRUE", VarusCardRegexes.Available().Match(Sqpp).Groups["v"].Value);
        Assert.Equal("2026-11-10", VarusCardRegexes.SpecialPriceFromDate().Match(Sqpp).Groups["v"].Value);
    }

    [Fact]
    public void PriceRegex_DoesNotMatchSpecialPriceKey()
    {
        Assert.False(VarusCardRegexes.Price().IsMatch("\"special_price\": 74.5"));
    }

    [Fact]
    public void LdJsonScript_ReturnsScriptBodies()
    {
        const string html = """
            <script type="text/javascript">var a = 1;</script>
            <SCRIPT data-x="1" type="application/ld+json">{"@type":"Product"}</SCRIPT>
            """;

        var match = Assert.Single(VarusCardRegexes.LdJsonScript().Matches(html));
        Assert.Equal("{\"@type\":\"Product\"}", match.Groups["json"].Value);
    }

    [Fact]
    public void Regexes_AreSingletons()
    {
        // The crash in run 234 came from compiling a new Regex for every product.
        Assert.Same(VarusCardRegexes.Price(), VarusCardRegexes.Price());
        Assert.Same(VarusCardRegexes.LdJsonScript(), VarusCardRegexes.LdJsonScript());
    }
}
