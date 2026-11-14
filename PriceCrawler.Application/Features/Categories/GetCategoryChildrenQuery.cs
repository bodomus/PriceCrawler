using PriceCrawler.Mediator;

namespace PriceCrawler.Application.Features.Categories;

/// <summary>
/// Reads one level of a store's category tree. Trees differ per store, so the store is part of
/// every category query.
/// </summary>
/// <param name="Store">Store key, for example <c>varus</c>.</param>
/// <param name="ParentId">Parent category id, or <see langword="null"/> for the top level.</param>
public sealed record GetCategoryChildrenQuery(string Store, long? ParentId) : IQuery<IReadOnlyList<CategoryNodeDto>>;
