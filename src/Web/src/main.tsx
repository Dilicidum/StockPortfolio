import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createRouter, RouterProvider } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { bootstrapSession } from './auth/bootstrapSession'
import { authStore } from './auth/authStore'
import { RouteErrorScreen } from './components/RouteErrorScreen'
import { Spinner } from './components/Spinner'
import './lib/i18n'
import { queryClient } from './lib/queryClient'
import { routeTree } from './routeTree.gen'
import './index.css'

const router = createRouter({
  routeTree,
  basepath: import.meta.env.BASE_URL,
  context: { queryClient, auth: authStore },
  defaultPreload: 'intent',
  defaultErrorComponent: RouteErrorScreen,
})

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}

function Splash() {
  const { t } = useTranslation('common')

  return (
    <div className="flex min-h-dvh items-center justify-center bg-bg">
      <Spinner size={22} label={t('splashLabel')} />
    </div>
  )
}

const container = document.getElementById('root')
if (!container) throw new Error('#root is missing from index.html')

const root = createRoot(container)
root.render(<Splash />)

void bootstrapSession().then(() => {
  root.render(
    <StrictMode>
      <RouterProvider router={router} />
    </StrictMode>,
  )
})
