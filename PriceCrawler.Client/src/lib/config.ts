/** Store whose catalog the site shows until a store switcher appears (category trees are per store). */
export const DEFAULT_STORE = "varus";

/**
 * Absolute base address of PriceCrawler.Api: `VITE_API_BASE_URL` when set, otherwise the page's own origin
 * (dev server proxy or a reverse proxy in front of both). Absolute, because `new Request()` outside the
 * browser rejects relative URLs.
 */
export const API_BASE_URL = resolveApiBaseUrl(import.meta.env.VITE_API_BASE_URL);

/**
 * Picks the API base address.
 * @param configured Value of `VITE_API_BASE_URL`, may be missing or empty.
 * @returns Configured address without a trailing slash, or the page origin.
 */
export function resolveApiBaseUrl(configured: string | undefined): string {
  const value = configured?.trim().replace(/\/+$/, "");
  return value !== undefined && value !== "" ? value : globalThis.location.origin;
}
