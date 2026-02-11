using VarPrice.Web.Crawler;
using Xunit;

namespace VarPrice.Web.Tests;

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

    [Theory]
    [InlineData("Цена 12,34 грн", 12.34)]
    [InlineData("Вартість 9.99 грн", 9.99)]
    public void PriceParser_Finds_Current_Price(string text, double expectedPrice)
    {
        var (price, old) = PriceParser.Parse(text);

        Assert.Equal((decimal)expectedPrice, price);
        Assert.Null(old);
    }

    [Fact]
    public void PriceParser_Finds_Old_Price_When_Marked()
    {
        var (price, old) = PriceParser.Parse("~~19,99~~ 15,49 грн");

        Assert.Equal(15.49m, price);
        Assert.Equal(19.99m, old);
    }

    [Theory]
    [InlineData("https://varus.ua/kyiv/ovochi/kartoplya", "kyiv")]
    [InlineData("https://varus.ua/lviv/ovochi", "lviv")]
    public void CityParser_Extracts_City_From_Url(string url, string expectedCity)
    {
        var city = CityParser.TryParseFromUrl(url);

        Assert.Equal(expectedCity, city);
    }
}
