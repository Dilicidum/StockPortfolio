import { beforeEach, describe, expect, it } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse, type RequestHandler } from 'msw'
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
import { dashboardKeys, type GetDashboardResult } from '../src/marketdata/dashboardApi'
import { alertsHandlers } from './msw/alerts'
import { marketDataHealthHandler } from './msw/dashboard'
import { dashboardSettingsHandler, saveDashboardSettingsHandler } from './msw/settings'
import { server } from './msw/server'

const dashboard: GetDashboardResult = {
  positions: [
    {
      id: '0199a1f0-0000-7000-8000-000000000001',
      ticker: 'AAPL',
      name: 'Apple Inc',
      quantity: 20,
      averagePrice: { amount: '125.000000', currency: 'USD' },
      cost: { amount: '2500.000000', currency: 'USD' },
      currentPrice: { amount: '150.0000', currency: 'USD' },
      marketValue: { amount: '3000.0000', currency: 'USD' },
      profit: { amount: '500.0000', currency: 'USD' },
      profitPercent: '20.00',
      weight: '100.00',
      observedAt: '2026-08-05T12:00:04+00:00',
      isLastKnown: false,
    },
    {
      id: '0199a1f0-0000-7000-8000-000000000002',
      ticker: 'TSLA',
      name: null,
      quantity: 5,
      averagePrice: { amount: '200.000000', currency: 'USD' },
      cost: { amount: '1000.000000', currency: 'USD' },
      currentPrice: null,
      marketValue: null,
      profit: null,
      profitPercent: null,
      weight: null,
      observedAt: null,
      isLastKnown: false,
    },
  ],
  totals: {
    value: { amount: '4242.4200', currency: 'USD' },
    cost: { amount: '3500.000000', currency: 'USD' },
    profit: { amount: '742.4200', currency: 'USD' },
    profitPercent: '21.21',
    positionCount: 2,
    pricedPositionCount: 1,
  },
  asOf: '2026-08-05T12:00:05+00:00',
  stalestObservedAt: '2026-08-05T12:00:04+00:00',
}

function freshCopy(): GetDashboardResult {
  const now = new Date().toISOString()

  return {
    ...dashboard,
    positions: dashboard.positions.map((position) =>
      position.observedAt ? { ...position, observedAt: now } : position,
    ),
    asOf: now,
    stalestObservedAt: now,
  }
}

beforeEach(() => {
  authStore.signOut()
  queryClient.clear()
  __resetRefreshInFlight()
})

const dashboardJson = (data: GetDashboardResult) =>
  http.get('*/api/dashboard', () => HttpResponse.json(data))

async function renderDashboard(handlers: RequestHandler[] = [dashboardJson(freshCopy())]) {
  authStore.setUser({ id: 'u-1', email: 'holder@example.com' })
  server.use(
    marketDataHealthHandler,
    dashboardSettingsHandler(),
    saveDashboardSettingsHandler,
    ...alertsHandlers,
    ...handlers,
  )

  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ['/dashboard'] }),
    context: { queryClient, auth: authStore },
    defaultPreload: false,
  })

  render(<RouterProvider router={router as AnyRouter} />)
  await screen.findByRole('heading', { name: 'Dashboard' })

  return router
}

const row = () => within(screen.getByRole('table'))

const refetchInterval = () =>
  queryClient.getQueryCache().find({ queryKey: dashboardKeys.view() })?.observers[0]?.options
    .refetchInterval

describe('dashboard', () => {
  it('renders totals from the API without client-side arithmetic', async () => {
    await renderDashboard()

    expect(await screen.findByText(/4.?242\.42/)).toBeInTheDocument()

    expect(screen.getByText(/21\.21%/)).toBeInTheDocument()

    await waitFor(() => expect(row().getByText('AAPL')).toBeInTheDocument())
  })

  it('renders a null price as pending, not $0.00', async () => {
    await renderDashboard()

    const tsla = await screen.findByRole('row', { name: /TSLA/ })

    expect(within(tsla).getAllByText('—')).toHaveLength(5)

    expect(within(tsla).queryAllByText(/^\D*0\.00$/)).toHaveLength(0)
  })

  it('renders a null totals percent as pending, not 0.00%', async () => {
    const zero = { amount: '0', currency: 'USD' }

    const nothingPriced: GetDashboardResult = {
      ...dashboard,
      positions: dashboard.positions.map((position) => ({
        ...position,
        currentPrice: null,
        marketValue: null,
        profit: null,
        profitPercent: null,
        weight: null,
        observedAt: null,
      })),
      totals: {
        value: zero,
        cost: zero,
        profit: zero,
        profitPercent: null,
        positionCount: 2,
        pricedPositionCount: 0,
      },
      stalestObservedAt: null,
    }

    await renderDashboard([dashboardJson(nothingPriced)])

    const tile = (await screen.findByText('Unrealised P&L')).parentElement
    expect(tile).not.toBeNull()

    expect(within(tile!).getByText('—')).toBeInTheDocument()
    expect(within(tile!).queryByText(/0\.00\s*%/)).not.toBeInTheDocument()
  })

  it('shows the amber freshness state when the prices behind the figures are stale', async () => {
    const stale: GetDashboardResult = {
      ...dashboard,
      asOf: new Date().toISOString(),
      stalestObservedAt: new Date(Date.now() - 1_800_000).toISOString(),
    }

    await renderDashboard([dashboardJson(stale)])

    const freshness = await screen.findByText(/prices up to \d+m old/i)
    expect(freshness).toHaveClass('text-warn')
  })

  it('changes refetchInterval when the interval control changes', async () => {
    const user = userEvent.setup()
    await renderDashboard()

    await waitFor(() => expect(refetchInterval()).toBe(60_000))

    await user.selectOptions(screen.getByLabelText(/refresh/i), '15')

    await waitFor(() => expect(refetchInterval()).toBe(15_000))
  })

  it('keeps the last good table on screen when a refresh fails', async () => {
    queryClient.setQueryData(dashboardKeys.view(), freshCopy())

    await renderDashboard([
      http.get('*/api/dashboard', () =>
        HttpResponse.json(
          { title: 'Bad gateway', detail: 'The quote provider is unavailable.', status: 404 },
          { status: 404, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    ])

    expect(await screen.findByRole('alert')).toHaveTextContent(/quote provider is unavailable/i)

    expect(row().getByText('AAPL')).toBeInTheDocument()
    expect(screen.getByText(/4.?242\.42/)).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Main' })).toBeInTheDocument()
  })
})
