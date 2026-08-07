import { createElement } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
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
import type { GetDashboardResult } from '../src/marketdata/dashboardApi'
import { alertsHandlers } from './msw/alerts'
import { dashboardSettingsHandler, saveDashboardSettingsHandler } from './msw/settings'
import { server } from './msw/server'

const panel = vi.hoisted(() => ({ throwing: true }))

vi.mock('../src/alerts/AlertPanel', () => ({
  AlertPanel: () => {
    if (panel.throwing) throw new Error('the activity panel exploded while rendering')
    return createElement('p', null, 'recent activity is back')
  },
}))

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
      observedAt: new Date().toISOString(),
      isLastKnown: false,
    },
  ],
  totals: {
    value: { amount: '3000.0000', currency: 'USD' },
    cost: { amount: '2500.000000', currency: 'USD' },
    profit: { amount: '500.0000', currency: 'USD' },
    profitPercent: '20.00',
    positionCount: 1,
    pricedPositionCount: 1,
  },
  asOf: new Date().toISOString(),
  stalestObservedAt: new Date().toISOString(),
}

beforeEach(() => {
  panel.throwing = true
  authStore.signOut()
  queryClient.clear()
  __resetRefreshInFlight()
})

async function renderDashboard() {
  authStore.setUser({ id: 'u-1', email: 'holder@example.com' })
  server.use(
    dashboardSettingsHandler(),
    saveDashboardSettingsHandler,
    ...alertsHandlers,
    http.get('*/api/dashboard', () => HttpResponse.json(dashboard)),
  )

  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ['/dashboard'] }),
    context: { queryClient, auth: authStore },
    defaultPreload: false,
  })

  render(<RouterProvider router={router as AnyRouter} />)
  await screen.findByRole('heading', { name: 'Dashboard' })
}

describe('the alerts panel boundary', () => {
  it('keeps the dashboard on screen when the panel throws while rendering', async () => {
    await renderDashboard()

    expect(await screen.findByText(/the activity panel stopped working/i)).toBeInTheDocument()

    await waitFor(() =>
      expect(within(screen.getByRole('table')).getByText('AAPL')).toBeInTheDocument(),
    )

    expect(within(screen.getByRole('table')).getByText(/3.?000\.00/)).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Main' })).toBeInTheDocument()
    expect(screen.getByText('Total value')).toBeInTheDocument()
  })

  it('clears its own error when retry is pressed', async () => {
    const user = userEvent.setup()
    await renderDashboard()

    await screen.findByText(/the activity panel stopped working/i)

    panel.throwing = false
    await user.click(screen.getByRole('button', { name: /try again/i }))

    expect(await screen.findByText('recent activity is back')).toBeInTheDocument()
    expect(screen.queryByText(/the activity panel stopped working/i)).not.toBeInTheDocument()
  })
})
