import { createRootRouteWithContext, Link, Outlet } from '@tanstack/react-router'
import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'
import { AuthProvider } from '../auth/AuthProvider'
import type { authStore } from '../auth/authStore'
import { queryClient } from '../lib/queryClient'

export interface RouterContext {
  queryClient: QueryClient
  /**
   * Deliberately the store itself, not a snapshot. `beforeLoad` is synchronous
   * and runs outside React, so it has to read the live value at guard time —
   * a snapshot captured when the router was created would be permanently stale.
   */
  auth: typeof authStore
}

export const Route = createRootRouteWithContext<RouterContext>()({
  component: RootLayout,
  notFoundComponent: NotFound,
})

function RootLayout() {
  return (
    <QueryClientProvider client={queryClient}>
      {/* AuthProvider sits inside the router tree because it calls
          useRouter() to invalidate route guards after a sign-in or sign-out. */}
      <AuthProvider>
        <Outlet />
      </AuthProvider>
    </QueryClientProvider>
  )
}

function NotFound() {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-3 bg-bg px-6 text-center text-tx">
      <p className="font-mono text-sm text-mu">404</p>
      <h1 className="text-xl font-semibold">That page does not exist.</h1>
      <Link to="/" className="text-ac text-sm hover:underline">
        Back to the app
      </Link>
    </div>
  )
}
