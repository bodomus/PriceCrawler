import type { SortingState } from "@tanstack/react-table";

import type { ProductGroup } from "@/api/client";
import { t } from "@/i18n";

/**
 * Converts the API sort expression (`-price,name`) into TanStack sorting state.
 * @param sort Sort expression; empty for the default order.
 * @returns Sorting state.
 */
export function parseSort(sort: string): SortingState {
  return sort
    .split(",")
    .map((term) => term.trim())
    .filter((term) => term !== "")
    .map((term) => (term.startsWith("-") ? { id: term.slice(1), desc: true } : { id: term, desc: false }));
}

/**
 * Converts TanStack sorting state into the API sort expression.
 * @param sorting Sorting state.
 * @returns Sort expression, e.g. `-priceChange,name`.
 */
export function formatSort(sorting: SortingState): string {
  return sorting.map(({ id, desc }) => (desc ? `-${id}` : id)).join(",");
}

/**
 * Display name of a group: the category name for category groups, otherwise a fixed text for the key.
 * @param group Group from the API.
 * @returns Name to show.
 */
export function groupName(group: ProductGroup): string {
  return group.name ?? t.products.groupNames[group.key] ?? group.key;
}
