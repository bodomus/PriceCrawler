using PriceCrawler.Application.Features.Listing;

namespace PriceCrawler.Application.Features.Products;

/// <summary>Fields of the product list that clients can sort and filter by.</summary>
public static class ProductListFields
{
    /// <summary>Product name.</summary>
    public static readonly ListField Name = new("name", ListFieldKind.Text);

    /// <summary>Product key on the store site.</summary>
    public static readonly ListField ExternalId = new("externalId", ListFieldKind.Text, Sortable: false);

    /// <summary>Latest price.</summary>
    public static readonly ListField Price = new("price", ListFieldKind.Number);

    /// <summary>Regular price before a promotion.</summary>
    public static readonly ListField OldPrice = new("oldPrice", ListFieldKind.Number);

    /// <summary>Whether the latest price is a promotion.</summary>
    public static readonly ListField IsPromo = new("isPromo", ListFieldKind.Boolean);

    /// <summary>Whether the product was in stock at the latest check.</summary>
    public static readonly ListField InStock = new("inStock", ListFieldKind.Boolean);

    /// <summary>When the latest price was observed.</summary>
    public static readonly ListField CapturedAt = new("capturedAt", ListFieldKind.DateTime);

    /// <summary>Price change since the previous day (absolute).</summary>
    public static readonly ListField PriceChange = new("priceChange", ListFieldKind.Number);

    /// <summary>Price change since the previous day in percent.</summary>
    public static readonly ListField PriceChangePercent = new("priceChangePercent", ListFieldKind.Number);

    /// <summary>The field set; default order is by name.</summary>
    public static readonly ListFieldSet Set = new(
        [Name, ExternalId, Price, OldPrice, IsPromo, InStock, CapturedAt, PriceChange, PriceChangePercent],
        [new SortTerm(Name, SortDirection.Ascending)]);
}
