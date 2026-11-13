namespace PriceCrawler.Application.Features.Categories;

/// <summary>One category of a store tree as the API returns it.</summary>
/// <param name="Id">Category id; pass it as <c>parentId</c> to load the children.</param>
/// <param name="ExternalId">Key of the category on the store site.</param>
/// <param name="Name">Display name.</param>
/// <param name="Url">Category page on the store site.</param>
/// <param name="ParentId">Parent id, or <see langword="null"/> for a top-level category.</param>
/// <param name="HasChildren">Whether the category has active subcategories.</param>
/// <param name="ProductCount">Active products in the category and all its subcategories.</param>
public sealed record CategoryNodeDto(
    long Id,
    string ExternalId,
    string Name,
    string Url,
    long? ParentId,
    bool HasChildren,
    int ProductCount);
