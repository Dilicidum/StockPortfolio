import { createRootRouteWithContext, Link, Outlet } from '@tanstack/react-router'
import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
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
  const { t } = useTranslation('common')

  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-3 bg-bg px-6 text-center text-tx">
      {/* Not translated: an HTTP-style status code, not a sentence. */}
      <p className="font-mono text-sm text-mu">404</p>
      <h1 className="text-xl font-semibold">{t('notFound.heading')}</h1>
      <Link to="/" className="text-ac text-sm hover:underline">
        {t('notFound.backLink')}
      </Link>
    </div>
  )
}
