import { queryOptions } from "@tanstack/react-query";

import { API_BASE_URL } from "@/lib/config";

import { api, unwrap } from "./client";

/** Query keys in one place, so invalidation never misses a spelling. */
export const queryKeys = {
  health: ["health"] as const,
  categoryChildren: (store: string, parentId: number | null) => ["categories", store, parentId] as const,
};

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
