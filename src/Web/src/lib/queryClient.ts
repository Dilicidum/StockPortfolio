import { QueryClient } from '@tanstack/react-query'
import { ApiError } from './apiClient'

/**
 * A module singleton rather than a `useState(() => new QueryClient())` inside a
 * component, because main.tsx has to run the session bootstrap through it
 * BEFORE any React tree exists (see main.tsx). `__root.tsx` then hands this
 * same instance to the tree via QueryClientProvider, so the bootstrap's cached
 * result is already warm on first render — no flash of "loading" for data we
 * fetched a moment ago.
 */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
      retry(failureCount, error) {
        // 4xx means the request was wrong, not unlucky. Retrying a 401 is
        // especially bad: apiFetch has already refreshed and retried once, so
        // a second 401 is a real logout, and hammering it delays the redirect.
        if (error instanceof ApiError && error.status >= 400 && error.status < 500) {
          return false
        }
        return failureCount < 2
      },
    },
    mutations: {
      retry: false,
    },
  },
})
