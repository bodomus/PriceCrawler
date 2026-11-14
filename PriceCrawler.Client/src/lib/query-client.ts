import { QueryClient } from "@tanstack/react-query";

/**
 * Creates the TanStack Query client with site-wide defaults.
 * @returns New query client.
 */
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { staleTime: 30_000, retry: 1, refetchOnWindowFocus: false },
    },
  });
}
