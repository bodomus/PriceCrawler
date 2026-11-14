import type { CategoryNode, ProductItem, ProductPage } from "@/api/client";

/**
 * Builds a category node with sensible defaults.
 * @param id Category id.
 * @param name Display name.
 * @param extra Fields to override.
 * @returns The node.
 */
export function category(id: number, name: string, extra: Partial<CategoryNode> = {}): CategoryNode {
  return {
    id,
    externalId: String(id),
    name,
    url: `https://varus.ua/${id}`,
    parentId: null,
    hasChildren: false,
    productCount: 10,
    ...extra,
  };
}

/**
 * Builds a product row with sensible defaults.
 * @param id Product id.
 * @param name Product name.
 * @param extra Fields to override.
 * @returns The row.
 */
export function product(id: number, name: string, extra: Partial<ProductItem> = {}): ProductItem {
  return {
    productId: id,
    externalId: String(id),
    name,
    url: `https://varus.ua/p${id}`,
    packValue: 1,
    packUnit: "кг",
    price: 30,
    oldPrice: null,
    isPromo: false,
    inStock: true,
    capturedAt: "2026-11-14T06:00:00Z",
    previousPrice: null,
    priceChange: null,
    priceChangePercent: null,
    trend: [],
    groupKey: null,
    ...extra,
  };
}

/**
 * Wraps rows into a single-page API response.
 * @param items Rows.
 * @param extra Fields to override (groups, totalCount, ...).
 * @returns The page.
 */
export function page(items: ProductItem[], extra: Partial<ProductPage> = {}): ProductPage {
  return {
    items,
    page: 1,
    pageSize: 50,
    totalCount: items.length,
    totalPages: items.length === 0 ? 0 : 1,
    groups: [],
    ...extra,
  };
}
