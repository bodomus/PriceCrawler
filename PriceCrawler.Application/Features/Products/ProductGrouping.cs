namespace PriceCrawler.Application.Features.Products;

/// <summary>How the product list is split into groups (the server sorts by the group first).</summary>
public enum ProductGrouping
{
    /// <summary>No groups.</summary>
    None,

    /// <summary>By the direct subcategory of the requested category (top-level category without one).</summary>
    Category,

    /// <summary>Promotion first, then regular price.</summary>
    Promo,

    /// <summary>In stock first, then out of stock.</summary>
    Stock,

    /// <summary>By the direction of the day's price change: down, up, same, unknown.</summary>
    Change,
}

/// <summary>Group keys and parsing of <see cref="ProductGrouping"/>.</summary>
public static class ProductGroupings
{
    /// <summary>Accepted values of the <c>groupBy</c> parameter, for messages and documentation.</summary>
    public const string Names = "category, promo, stock, change";

    /// <summary>Group key of products with a promotion.</summary>
    public const string PromoKey = "promo";

    /// <summary>Group key of products at the regular price.</summary>
    public const string RegularKey = "regular";

    /// <summary>Group key of products in stock.</summary>
    public const string InStockKey = "inStock";

    /// <summary>Group key of products out of stock.</summary>
    public const string OutOfStockKey = "outOfStock";

    /// <summary>Group key of products whose price went down since the previous day.</summary>
    public const string DownKey = "down";

    /// <summary>Group key of products whose price went up since the previous day.</summary>
    public const string UpKey = "up";

    /// <summary>Group key of products whose price did not change since the previous day.</summary>
    public const string SameKey = "same";

    /// <summary>Group key of products without a previous-day price to compare with.</summary>
    public const string UnknownKey = "unknown";

    /// <summary>Group key of products of the requested category that sit in none of its subcategories.</summary>
    public const string NoSubcategoryKey = "none";

    /// <summary>Parses the <c>groupBy</c> query parameter.</summary>
    /// <param name="value">Parameter value; <see langword="null"/> or empty means no grouping.</param>
    /// <param name="grouping">Parsed grouping.</param>
    /// <returns><see langword="true"/> when the value is known.</returns>
    public static bool TryParse(string? value, out ProductGrouping grouping)
    {
        grouping = value?.Trim().ToUpperInvariant() switch
        {
            null or "" or "NONE" => ProductGrouping.None,
            "CATEGORY" => ProductGrouping.Category,
            "PROMO" => ProductGrouping.Promo,
            "STOCK" => ProductGrouping.Stock,
            "CHANGE" => ProductGrouping.Change,
            _ => (ProductGrouping)(-1),
        };
        return Enum.IsDefined(grouping);
    }
}
