using PriceCrawler.Infrastructure.Crawler;

namespace PriceCrawler.Web.Tests;

/// <summary>
/// MPC-92: the price of the main product must come from its own sqpp object, never from the
/// related-products carousel. Pages follow the layout of real varus.ua pages saved on 12.11.2026.
/// </summary>
public sealed class VarusProductPricesTests
{
    /// <summary>Builds a page like varus.ua: config, JSON-LD, main sku + sqpp, store list, carousel, visible carousel price.</summary>
    internal static string Page(string sku, string ldPrice, string mainSqpp) =>
        $$$"""
        <html><head>
        <script>window.cfg={"sqpp_score":1,"sqpp_data_region":"kyiv"};</script>
        <script type="application/ld+json">{"@type":"Product","name":"Main","sku":"{{{sku}}}","offers":{"price":"{{{ldPrice}}}","priceCurrency":"UAH"}}</script>
        </head><body>
        <div class="sf-price">337.50    239 ₴</div>
        <script>window.__STATE__={"product":{"sku":"{{{sku}}}","name":"Main","extension":{"a":1},"sqpp":{{{mainSqpp}}},
        "stores":[{"id":37,"name":"Kyiv","price":20.2,"special_price":20.2}]},
        "related":[{"sku":"2665327","sqpp":{"price":337.5,"special_price":239,"available":true}},
                   {"sku":"2664163","sqpp":{"price":149,"special_price":0,"available":true}},
                   {"sku":"2664107","sqpp":{"price":679.9,"special_price":0,"available":true}}]};</script>
        </body></html>
        """;

    [Theory]
    [Trait("Category", "Unit")]
    // pasta 7728: special_price null -> the old parser took a store or carousel price (149)
    [InlineData("2634005", "28.9", """{"price":28.9,"special_price":null,"available":true}""", 28.9, 28.9, false)]
    // wine 6089: special_price 0 -> the old parser fell back to the first "₴" on the page (337.50 / 329)
    [InlineData("2586678", "162", """{"price":162,"special_price":0,"special_price_discount":0,"available":true}""", 162, 162, false)]
    // salo 5785: real promo
    [InlineData("2647022", "165.7", """{"price":194.9,"special_price":165.7,"special_price_discount":15,"available":true}""", 165.7, 194.9, true)]
    // sausage 5887: real promo, values as strings
    [InlineData("2663677", "239.9", """{"price":"359.90","special_price":"239.90","available":"true"}""", 239.9, 359.9, true)]
    public void Resolve_UsesOnlyTheMainProduct(string sku, string ldPrice, string sqpp, double price, double oldPrice, bool promo)
    {
        var html = Page(sku, ldPrice, sqpp);

        var main = VarusProductPrices.FindMainSqpp(html, sku);
        var resolved = VarusProductPrices.Resolve(decimal.Parse(ldPrice, System.Globalization.CultureInfo.InvariantCulture), main);

        Assert.NotNull(main);
        Assert.Equal((decimal)price, resolved.Price);
        Assert.Equal((decimal)oldPrice, resolved.OldPrice);
        Assert.Equal(promo, resolved.PromoFlag);
        Assert.True(resolved.InStock);
        Assert.False(resolved.OldPriceDropped);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FindMainSqpp_IgnoresNestedPriceKeys()
    {
        var html = Page("1", "50", """{"tier":{"price":1,"special_price":2},"price":60,"special_price":50}""");

        var main = VarusProductPrices.FindMainSqpp(html, "1");

        Assert.Equal(new VarusSqpp(60m, 50m, null, null), main);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FindMainSqpp_SkuWithoutOwnSqpp_ReturnsNull()
    {
        var html = Page("1", "50", "null");

        Assert.Null(VarusProductPrices.FindMainSqpp(html, "1"));
        Assert.Null(VarusProductPrices.FindMainSqpp(html, "999"));
        Assert.Null(VarusProductPrices.FindMainSqpp(html, ""));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Resolve_WithoutJsonLd_UsesPromoThenRegularPrice()
    {
        Assert.Equal(80m, VarusProductPrices.Resolve(null, new VarusSqpp(100m, 80m, null, true)).Price);
        Assert.Equal(100m, VarusProductPrices.Resolve(null, new VarusSqpp(100m, 0m, null, true)).Price);
        Assert.Null(VarusProductPrices.Resolve(null, null).Price);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Resolve_RegularBelowPrice_DropsOldPrice()
    {
        var resolved = VarusProductPrices.Resolve(149m, new VarusSqpp(28.9m, null, null, true));

        Assert.Equal(149m, resolved.Price);
        Assert.Null(resolved.OldPrice);
        Assert.False(resolved.PromoFlag);
        Assert.True(resolved.OldPriceDropped);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Resolve_PromoWithoutDiscountField_CalculatesDiscount()
    {
        var resolved = VarusProductPrices.Resolve(165.7m, new VarusSqpp(194.9m, 165.7m, null, true));

        Assert.Equal(15, resolved.DiscountPercent);
        Assert.True(resolved.PromoFlag);
    }
}
