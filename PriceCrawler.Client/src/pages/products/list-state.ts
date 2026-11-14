import { useCallback, useMemo } from "react";
import { useSearchParams } from "react-router";

import type { ProductGroupBy, ProductListParams } from "@/api/queries";

/** Page sizes offered in the pager. */
export const PAGE_SIZES = [25, 50, 100, 200] as const;

/** Page size when the address has none. */
export const DEFAULT_PAGE_SIZE = 50;

const GROUPINGS: readonly ProductGroupBy[] = ["category", "promo", "stock", "change"];

/**
 * Reads the product list parameters from the address query string, so a link or a reload keeps
 * the category, search, sort, page and grouping. Invalid values fall back to defaults.
 * @param params Query string of the page.
 * @returns List parameters.
 */
export function parseListParams(params: URLSearchParams): ProductListParams {
  const category = Number(params.get("category"));
  const page = Number(params.get("page"));
  const size = Number(params.get("size"));
  const group = params.get("group");
  return {
    categoryId: Number.isSafeInteger(category) && category > 0 ? category : null,
    search: params.get("q") ?? "",
    sort: params.get("sort") ?? "",
    page: Number.isSafeInteger(page) && page > 0 ? page : 1,
    pageSize: (PAGE_SIZES as readonly number[]).includes(size) ? size : DEFAULT_PAGE_SIZE,
    groupBy: GROUPINGS.find((value) => value === group) ?? null,
  };
}

/**
 * Writes list parameters into a query string, leaving defaults out to keep links short.
 * @param list List parameters.
 * @returns Query string parameters.
 */
export function formatListParams(list: ProductListParams): URLSearchParams {
  const params = new URLSearchParams();
  if (list.categoryId !== null) params.set("category", String(list.categoryId));
  if (list.search !== "") params.set("q", list.search);
  if (list.sort !== "") params.set("sort", list.sort);
  if (list.groupBy !== null) params.set("group", list.groupBy);
  if (list.pageSize !== DEFAULT_PAGE_SIZE) params.set("size", String(list.pageSize));
  if (list.page !== 1) params.set("page", String(list.page));
  return params;
}

/**
 * Product list parameters kept in the address bar.
 * @returns Current parameters and an updater; every change except paging returns to page 1.
 */
export function useProductListState(): [ProductListParams, (change: Partial<ProductListParams>) => void] {
  const [searchParams, setSearchParams] = useSearchParams();
  const list = useMemo(() => parseListParams(searchParams), [searchParams]);
  const update = useCallback(
    (change: Partial<ProductListParams>) => {
      setSearchParams(
        (current) => {
          const previous = parseListParams(current);
          const next = { ...previous, ...change };
          if (change.page === undefined) next.page = 1;
          return formatListParams(next);
        },
        { replace: change.search !== undefined },
      );
    },
    [setSearchParams],
  );
  return [list, update];
}
