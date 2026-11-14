import { QueryClient } from "@tanstack/react-query";
import { render, type RenderResult } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { vi } from "vitest";

import { Providers } from "@/app/providers";
import { routes } from "@/app/router";

/**
 * Renders the whole app at a given address with a fresh, non-retrying query client.
 * @param path Initial address, e.g. `/products`.
 * @returns Testing Library render result.
 */
export function renderApp(path = "/"): RenderResult {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(routes, { initialEntries: [path] });
  return render(
    <Providers queryClient={queryClient}>
      <RouterProvider router={router} />
    </Providers>,
  );
}

/**
 * Replaces `fetch` with a stub that answers by URL path.
 * @param handlers Map of path (without query string) to JSON body; unknown paths answer 404.
 * @returns The mock, to inspect calls.
 */
export function stubFetch(handlers: Record<string, unknown>) {
  const mock = vi.fn((input: RequestInfo | URL) => {
    const raw = typeof input === "string" ? input : "url" in input ? input.url : input.href;
    const url = new URL(raw, "http://localhost");
    const body = handlers[url.pathname];
    return Promise.resolve(
      body === undefined
        ? new Response(JSON.stringify({ title: "Not Found", status: 404 }), {
            status: 404,
            headers: { "Content-Type": "application/problem+json" },
          })
        : new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } }),
    );
  });
  vi.stubGlobal("fetch", mock);
  return mock;
}
