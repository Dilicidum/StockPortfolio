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
    // The app's own bootstrap has already settled by the time the real router
    // mounts, so there is nothing to preload here either.
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

    // And it remembers where the visitor was heading, so signing in finishes
    // the journey instead of dumping them on a generic landing page.
    expect(router.state.location.search).toMatchObject({ redirect: '/dashboard' })

    expect(await screen.findByRole('button', { name: /sign in/i })).toBeInTheDocument()
    expect(screen.getByLabelText(/email/i)).toBeInTheDocument()
  })
})

describe('login form', () => {
  it('renders a server error without crashing', async () => {
    // The API rejects the credentials with RFC 7807 problem+json, which is the
    // ordinary outcome of a typo — not an exception, and not a blank screen.
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

    // Still on the login page, form still usable, no error boundary.
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
            // ASP.NET Core emits PascalCase keys; the client normalises them.
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
      // Phase 3 gave /dashboard a real fetch and Phase 4 gave the authenticated layout an
      // alert stream, and MSW is set to error on anything unhandled — so this sign-out
      // test needs both sets of stubs to reach the page.
      ...dashboardHandlers,
      ...alertsHandlers,
    )

    authStore.setUser({ email: 'holder@example.com' })

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
