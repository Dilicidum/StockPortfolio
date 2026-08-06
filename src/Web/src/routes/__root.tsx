import { createRootRouteWithContext, Link, Outlet } from '@tanstack/react-router'
import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { AuthProvider } from '../auth/AuthProvider'
import type { authStore } from '../auth/authStore'
import { queryClient } from '../lib/queryClient'

export interface RouterContext {
  queryClient: QueryClient
  auth: typeof authStore
}

export const Route = createRootRouteWithContext<RouterContext>()({
  component: RootLayout,
  notFoundComponent: NotFound,
})

function RootLayout() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <Outlet />
      </AuthProvider>
    </QueryClientProvider>
  )
}

function NotFound() {
  const { t } = useTranslation('common')

  return (
    <div className="flex min-h-dvh flex-col items-center justify-center gap-3 bg-bg px-6 text-center text-tx">
      <p className="font-mono text-sm text-mu">404</p>
      <h1 className="text-xl font-semibold">{t('notFound.heading')}</h1>
      <Link to="/" className="text-ac text-sm hover:underline">
        {t('notFound.backLink')}
      </Link>
    </div>
  )
}
