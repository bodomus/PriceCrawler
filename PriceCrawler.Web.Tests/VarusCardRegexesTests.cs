using PriceCrawler.Infrastructure.Crawler;

namespace PriceCrawler.Web.Tests;

/// <summary>Tests for the build-time generated regular expressions of the product card parser (MPC-109).</summary>
public sealed class VarusCardRegexesTests
{
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
        Assert.Same(VarusCardRegexes.LdJsonScript(), VarusCardRegexes.LdJsonScript());
    }
}
