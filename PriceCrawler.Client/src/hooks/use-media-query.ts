import { useSyncExternalStore } from "react";

/**
 * Tracks a CSS media query.
 * @param query Media query, e.g. `(min-width: 768px)`.
 * @returns Whether the query currently matches.
 */
export function useMediaQuery(query: string): boolean {
  return useSyncExternalStore(
    (onChange) => {
      const list = window.matchMedia(query);
      list.addEventListener("change", onChange);
      return () => {
        list.removeEventListener("change", onChange);
      };
    },
    () => window.matchMedia(query).matches,
    () => false,
  );
}
