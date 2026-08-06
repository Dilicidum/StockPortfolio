import { beforeEach, expect, it } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import {
  createMemoryHistory,
  createRouter,
  RouterProvider,
  type AnyRouter,
} from '@tanstack/react-router'
import { routeTree } from '../src/routeTree.gen'
import { authStore } from '../src/auth/authStore'
import { bootstrapSession } from '../src/auth/bootstrapSession'
import { queryClient } from '../src/lib/queryClient'
import { __resetRefreshInFlight } from '../src/lib/apiClient'
import { clearTokens } from '../src/lib/tokenStore'
import { alertsHandlers } from './msw/alerts'
import { dashboardHandlers } from './msw/dashboard'
import { server } from './msw/server'

beforeEach(() => {
  authStore.signOut()
  clearTokens()
  queryClient.clear()
  __resetRefreshInFlight()
})

/**
 * P0: a hard refresh of a guarded route must keep you signed in.
 *
 * This mirrors main.tsx exactly — await `bootstrapSession()`, then mount the
 * router — and it calls the app's own bootstrap function rather than a stand-in,
 * because the failure mode being guarded against is an ordering mistake and a
 * re-implementation would order things correctly by accident.
 *
 * Invert the two statements below and this test fails the way the real app
 * would: `beforeLoad` sees no session and bounces to /login.
 */
it('keeps a refreshable session on a hard load of /dashboard', async () => {
  server.use(
    http.post('*/api/auth/refresh', () =>
      HttpResponse.json({
        accessToken: 'fresh-token',
        refreshToken: 'rotated',
        expiresIn: 900,
      }),
    ),
    http.get('*/api/auth/manage/info', () =>
      HttpResponse.json({ id: 'u-1', email: 'holder@example.com' }),
    ),
    // The route this test lands on fetches since Phase 3, and since Phase 4 its layout
    // also opens the alert stream. MSW errors on anything unhandled — without these the
    // session assertion would fail for an unrelated reason.
    ...dashboardHandlers,
    ...alertsHandlers,
  )

  await bootstrapSession()
  expect(authStore.getState().isAuthenticated).toBe(true)

  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ['/dashboard'] }),
    context: { queryClient, auth: authStore },
    defaultPreload: false,
  })
  render(<RouterProvider router={router as AnyRouter} />)

  await waitFor(() => {
    expect(router.state.location.pathname).toBe('/dashboard')
  })
  expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
})

it('falls through to /login when the refresh token is rejected', async () => {
  server.use(
    http.post('*/api/auth/refresh', () =>
      HttpResponse.json(
        { title: 'Unauthorized', status: 401 },
        { status: 401, headers: { 'Content-Type': 'application/problem+json' } },
      ),
    ),
  )

  // A rejected refresh must resolve, not throw. If bootstrapSession ever starts
  // rejecting, main.tsx never calls root.render and the app hangs on the splash
  // forever — a white screen with no console error.
  await expect(bootstrapSession()).resolves.toBeUndefined()
  expect(authStore.getState().isAuthenticated).toBe(false)

  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ['/dashboard'] }),
    context: { queryClient, auth: authStore },
    defaultPreload: false,
  })
  render(<RouterProvider router={router as AnyRouter} />)

  await waitFor(() => {
    expect(router.state.location.pathname).toBe('/login')
  })
})
