using System.Text.RegularExpressions;

namespace PriceCrawler.Application.Features;

/// <summary>Format of store keys (<c>category.source</c>, <c>product_catalog.source</c>), e.g. <c>varus</c>.</summary>
public static partial class StoreKeys
{
    /// <summary>Message for an invalid store key.</summary>
    public const string FormatMessage = "Store must be 1-50 characters: lowercase Latin letters, digits or '-'.";

    /// <summary>Checks that a value is a well-formed store key.</summary>
    /// <param name="value">Value to check.</param>
    /// <returns><see langword="true"/> when the value can be a store key.</returns>
    public static bool IsValid(string? value) => value is not null && StoreKeyPattern().IsMatch(value);

    /// <summary>Lowercase Latin letters, digits and hyphens, 1 to 50 characters (the column is varchar(50)).</summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex("^[a-z0-9-]{1,50}$", RegexOptions.CultureInvariant)]
    private static partial Regex StoreKeyPattern();
}
