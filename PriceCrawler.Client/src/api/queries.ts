import { keepPreviousData, queryOptions } from "@tanstack/react-query";

import { API_BASE_URL } from "@/lib/config";

import { api, unwrap } from "./client";

/** Query keys in one place, so invalidation never misses a spelling. */
export const queryKeys = {
  health: ["health"] as const,
  categoryChildren: (store: string, parentId: number | null) => ["categories", store, parentId] as const,
  products: (store: string, params: ProductListParams) => ["products", store, params] as const,
};

/** How the product list can be grouped on the server. */
export type ProductGroupBy = "category" | "promo" | "stock" | "change";

/** Parameters of the product list request (see `GET /api/v1/stores/{store}/products`). */
export interface ProductListParams {
  /** Category with its subcategories, or `null` for the whole store. */
  categoryId: number | null;
  /** Text the name must contain; empty for any. */
  search: string;
  /** Sort expression, e.g. `-priceChange`; empty for the default order. */
  sort: string;
  /** 1-based page number. */
  page: number;
  /** Rows per page. */
  pageSize: number;
  /** Grouping, or `null` for none. */
  groupBy: ProductGroupBy | null;
}

/**
 * Query of one page of a store's products. The previous page stays on screen while the next one loads.
 * @param store Store key, e.g. `varus`.
 * @param params Category, search, sort, page and grouping.
 * @returns TanStack Query options.
 */
export function productsQuery(store: string, params: ProductListParams) {
  return queryOptions({
    queryKey: queryKeys.products(store, params),
    queryFn: ({ signal }) => {
      const search = params.search.trim();
      return unwrap(
        api.GET("/api/v1/stores/{store}/products", {
          params: {
            path: { store },
            query: {
              page: params.page,
              pageSize: params.pageSize,
              ...(params.categoryId === null ? {} : { categoryId: params.categoryId }),
              ...(params.sort === "" ? {} : { sort: params.sort }),
              ...(params.groupBy === null ? {} : { groupBy: params.groupBy }),
              ...(search === "" ? {} : { filter: [`name:contains:${search}`] }),
            },
          },
          signal,
        }),
      );
    },
    placeholderData: keepPreviousData,
  });
}

/**
 * Query of one level of a store's category tree.
 * @param store Store key, e.g. `varus`.
 * @param parentId Parent category id, `null` for the top level.
 * @returns TanStack Query options.
 */
export function categoryChildrenQuery(store: string, parentId: number | null = null) {
  return queryOptions({
    queryKey: queryKeys.categoryChildren(store, parentId),
    queryFn: ({ signal }) =>
      unwrap(
        api.GET("/api/v1/stores/{store}/categories", {
          params: { path: { store }, query: parentId === null ? {} : { parentId } },
          signal,
        }),
      ),
    staleTime: 5 * 60_000,
  });
}

/**
 * Liveness check of the API (`/health` is not part of the OpenAPI contract, so plain fetch).
 * Polled every 30 s for the status dot in the header.
 * @returns TanStack Query options; data is `true` when the API answered 2xx.
 */
export function healthQuery() {
  return queryOptions({
    queryKey: queryKeys.health,
    queryFn: async ({ signal }) => {
      const response = await fetch(`${API_BASE_URL}/health`, { signal });
      return response.ok;
    },
    refetchInterval: 30_000,
    retry: false,
  });
}
