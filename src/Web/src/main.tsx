import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createRouter, RouterProvider } from '@tanstack/react-router'
import { bootstrapSession } from './auth/bootstrapSession'
import { authStore } from './auth/authStore'
import { Spinner } from './components/Spinner'
import './lib/i18n'
import { queryClient } from './lib/queryClient'
import { routeTree } from './routeTree.gen'
import './index.css'

/**
 * `basepath` is derived from `import.meta.env.BASE_URL`, which Vite sets from
 * the `base` option, which comes from the VITE_BASE environment variable. Three
 * links in one chain so the router and the asset URLs can never disagree:
 * nginx serves the compose SPA at "/", GitHub Pages serves it at "/<repo>/",
 * and neither value is written down anywhere in the source.
 */
const router = createRouter({
  routeTree,
  basepath: import.meta.env.BASE_URL,
  context: { queryClient, auth: authStore },
  defaultPreload: 'intent',
})

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}

function Splash() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-bg">
      <Spinner size={22} label="Restoring session" />
    </div>
  )
}

/**
 * ─────────────────────────────────────────────────────────────────────────────
 * BOOTSTRAP BEFORE MOUNT — the single most important twelve lines in this app.
 * ─────────────────────────────────────────────────────────────────────────────
 *
 * TanStack Router's `beforeLoad` is synchronous. A React effect runs after the
 * first render. Put the session restore in an effect and the ordering is:
 *
 *      render -> beforeLoad sees isAuthenticated === false -> redirect /login
 *      -> effect finally runs -> session restored, user already on /login
 *
 * which means a hard refresh of /dashboard always bounces to the login page.
 * That is the P0 session-persistence criterion failing, and it fails in a way
 * no test catches unless the test does a full reload, because in-app navigation
 * works perfectly.
 *
 * So: run the refresh imperatively here, render a splash while it settles, and
 * only mount <RouterProvider> once the answer is known. By the time any guard
 * runs, `authStore.getState()` is final.
 *
 * On StrictMode: this is not an effect, so React cannot double-invoke it. The
 * async work starts exactly once, before any component exists. (The equivalent
 * hazard inside an effect — phase 4's SSE hook — needs a `cancelled` flag and
 * cleanup; the refresh call in apiClient is separately deduped by a shared
 * in-flight promise, which covers a double module evaluation too.)
 *
 * `bootstrapSession` itself lives in auth/bootstrapSession.ts so the test suite
 * can exercise the same function the app runs, rather than a copy of it.
 */
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
