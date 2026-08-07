import { beforeEach, describe, expect, it } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import {
  createMemoryHistory,
  createRouter,
  RouterProvider,
  type AnyRouter,
} from '@tanstack/react-router'
import { routeTree } from '../src/routeTree.gen'
import { authStore } from '../src/auth/authStore'
import { queryClient } from '../src/lib/queryClient'
import { __resetRefreshInFlight } from '../src/lib/apiClient'
import { alertsHandlers } from './msw/alerts'
import { dashboardHandlers } from './msw/dashboard'
import { server } from './msw/server'

function renderAt(path: string) {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: [path] }),
    context: { queryClient, auth: authStore },
    defaultPreload: false,
  })

  render(<RouterProvider router={router as AnyRouter} />)
  return router
}

beforeEach(() => {
  authStore.signOut()
  queryClient.clear()
  __resetRefreshInFlight()
})

describe('route guard', () => {
  it('redirects an unauthenticated visit to /dashboard to the login page', async () => {
    const router = renderAt('/dashboard')

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/login')
    })

    expect(router.state.location.search).toMatchObject({ redirect: '/dashboard' })

    expect(await screen.findByRole('button', { name: /sign in/i })).toBeInTheDocument()
    expect(screen.getByLabelText(/email/i)).toBeInTheDocument()
  })
})

describe('login form', () => {
  it('renders a server error without crashing', async () => {
    server.use(
      http.post('*/api/auth/login', () =>
        HttpResponse.json(
          {
            type: 'https://tools.ietf.org/html/rfc9110#section-15.5.2',
            title: 'Unauthorized',
            status: 401,
            detail: 'Invalid email or password.',
          },
          { status: 401, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )

    const user = userEvent.setup()
    const router = renderAt('/login')

    await screen.findByRole('button', { name: /sign in/i })

    await user.type(screen.getByLabelText(/email/i), 'someone@example.com')
    await user.type(screen.getByLabelText(/password/i), 'not-the-password')
    await user.click(screen.getByRole('button', { name: /sign in/i }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Invalid email or password.')

    expect(router.state.location.pathname).toBe('/login')
    expect(screen.getByLabelText(/email/i)).toHaveValue('someone@example.com')
    expect(authStore.getState().isAuthenticated).toBe(false)
  })

  it('surfaces field-level errors from a 400 under the field they name', async () => {
    server.use(
      http.post('*/api/auth/login', () =>
        HttpResponse.json(
          {
            title: 'One or more validation errors occurred.',
            status: 400,
            errors: { Email: ['Email is not a known account.'] },
          },
          { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )

    const user = userEvent.setup()
    renderAt('/login')

    await screen.findByRole('button', { name: /sign in/i })
    await user.type(screen.getByLabelText(/email/i), 'someone@example.com')
    await user.type(screen.getByLabelText(/password/i), 'whatever1')
    await user.click(screen.getByRole('button', { name: /sign in/i }))

    expect(await screen.findByText('Email is not a known account.')).toBeInTheDocument()
    expect(screen.getByLabelText(/email/i)).toHaveAttribute('aria-invalid', 'true')
  })
})

describe('signed-in session', () => {
  it('lets an authenticated visitor reach the dashboard and sign out again', async () => {
    let logoutCalls = 0
    server.use(
      http.post('*/api/auth/logout', () => {
        logoutCalls += 1
        return new HttpResponse(null, { status: 204 })
      }),
      ...dashboardHandlers,
      ...alertsHandlers,
    )

    authStore.setUser({ id: 'u-1', email: 'holder@example.com' })

    const user = userEvent.setup()
    const router = renderAt('/dashboard')

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/dashboard')
    })
    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
    expect(screen.getAllByText('holder@example.com').length).toBeGreaterThan(0)

    await user.click(screen.getAllByRole('button', { name: /sign out/i })[0]!)

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/login')
    })
    expect(logoutCalls).toBe(1)
    expect(authStore.getState().isAuthenticated).toBe(false)
  })
})
