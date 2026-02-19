using System;
using System.Linq;

namespace VarPrice.Web.Crawler;

public interface IProductUrlFilter
{
    bool IsProductUrl(Uri url);
    bool IsTestUrl(Uri url);
}

public sealed class VarusProductUrlFilter : IProductUrlFilter
{
    private static readonly string[] BlockMarkers = ["/blog", "/novosti", "/news", "/akcii", "/test1"];

    // Для VARUS: продукты часто выглядят как "/<длинный-slug>" (много дефисов),
    // а категории — короче и без "единиц"/чисел.
    public bool IsProductUrl(Uri url)
    {
        var path = Uri.UnescapeDataString(url.AbsolutePath);

        if (BlockMarkers.Any(m => path.Contains(m, StringComparison.OrdinalIgnoreCase)))
            return false;

        // эвристика: "slug" обычно длинный и с дефисами
        var last = path.Trim('/');

        // если есть подкаталоги — чаще это не продукт, но зависит от сайта
        if (last.Contains('/'))
            return false;

        var dashCount = last.Count(c => c == '-');
        if (dashCount >= 3) // подберите порог под ваш sitemap
            return true;

        // опционально: если встречаются числа/единицы (ml, g, kg, l)
        if (last.Any(char.IsDigit) &&
            (last.Contains("ml", StringComparison.OrdinalIgnoreCase) ||
             last.Contains("g", StringComparison.OrdinalIgnoreCase) ||
             last.Contains("kg", StringComparison.OrdinalIgnoreCase) ||
             last.Contains("l", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    public bool IsTestUrl(Uri url)
    {
        var path = url.AbsolutePath;
        return BlockMarkers.Any(m => path.Contains(m, StringComparison.OrdinalIgnoreCase));
    }
}

