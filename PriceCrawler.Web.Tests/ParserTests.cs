using PriceCrawler.Infrastructure.Crawler;

namespace PriceCrawler.Web.Tests;

public sealed class ParserTests
{
    [Theory]
    [InlineData("Картопля 2 кг", 2.0, "кг")]
    [InlineData("Молоко 950 мл", 950.0, "мл")]
    [InlineData("Цукор 1.5 кг", 1.5, "кг")]
    [InlineData("Сіль 0,2 кг", 0.2, "кг")]
    public void PackParser_Parses_Value_And_Unit(string text, double expectedValue, string expectedUnit)
    {
        var (value, unit) = PackParser.TryParse(text);
        Assert.Equal(expectedUnit, unit);
        Assert.Equal((decimal)expectedValue, value);
    }

    [Fact]
    public void PackParser_Parses_QuantityText_Into_Unit()
    {
        var text = "\"quantityText\":\"за 1 шт (400 г)\"";
        var (value, unit) = PackParser.TryParse(text);
        Assert.Equal(400m, value);
        Assert.Equal("за 1 шт (400 г)", unit);
    }
}
