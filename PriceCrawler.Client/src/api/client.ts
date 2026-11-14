import createClient from "openapi-fetch";

import { API_BASE_URL } from "@/lib/config";

import type { components, paths } from "./schema";

/** Typed HTTP client of PriceCrawler.Api; paths, parameters and responses come from the OpenAPI file. */
export const api = createClient<paths>({
  baseUrl: API_BASE_URL,
  // Resolve fetch on every call (not once at import), so tests and tools can replace it.
  fetch: (request) => globalThis.fetch(request),
});

/** Schemas (DTOs) of the API. */
export type Schemas = components["schemas"];

/** One node of a store's category tree. */
export type CategoryNode = Schemas["CategoryNodeDto"];

/** Error thrown by query functions when the API answers with a non-success status. */
export class ApiError extends Error {
  /** HTTP status code, 0 when the request did not reach the API. */
  readonly status: number;

  /**
   * Creates the error.
   * @param status HTTP status code.
   * @param message Human-readable text (problem `title` when the API gave one).
   */
  constructor(status: number, message: string) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

/**
 * Unwraps an openapi-fetch result: returns the data or throws {@link ApiError}.
 * @param result Result of an `api.GET(...)` call.
 * @returns Response body.
 */
export async function unwrap<T>(result: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<T> {
  const { data, error, response } = await result;
  if (!response.ok || data === undefined) {
    const title =
      typeof error === "object" && error !== null && "title" in error && typeof error.title === "string"
        ? error.title
        : response.statusText;
    throw new ApiError(response.status, title || `HTTP ${response.status}`);
  }
  return data;
}
