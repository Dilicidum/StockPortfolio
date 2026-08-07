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

it('keeps a refreshable session on a hard load of /dashboard', async () => {
  server.use(
    http.post('*/api/auth/refresh', () =>
      HttpResponse.json({
        accessToken: 'fresh-token',
        refreshToken: 'rotated',
        expiresIn: 900,
      }),
    ),
    http.get('*/api/auth/me', () =>
      HttpResponse.json({ id: 'u-1', email: 'holder@example.com' }),
    ),
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
