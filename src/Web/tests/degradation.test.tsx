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
import type { GetDashboardResult } from '../src/marketdata/dashboardApi'
import { alertsHandlers } from './msw/alerts'
import { healthDetailHandler } from './msw/health'
import { dashboardSettingsHandler, saveDashboardSettingsHandler } from './msw/settings'
import { server } from './msw/server'

const priced: GetDashboardResult = {
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

const zero = { amount: '0', currency: 'USD' }

const nothingPriced: GetDashboardResult = {
  positions: [
    {
      ...priced.positions[0]!,
      currentPrice: null,
      marketValue: null,
      profit: null,
      profitPercent: null,
      weight: null,
      observedAt: null,
    },
  ],
  totals: {
    value: zero,
    cost: { amount: '2500.000000', currency: 'USD' },
    profit: zero,
    profitPercent: null,
    positionCount: 1,
    pricedPositionCount: 0,
  },
  asOf: new Date().toISOString(),
  stalestObservedAt: null,
}

const lastKnown: GetDashboardResult = {
  ...priced,
  positions: [{ ...priced.positions[0]!, isLastKnown: true, observedAt: null }],
}

beforeEach(() => {
  authStore.signOut()
  queryClient.clear()
  __resetRefreshInFlight()
})

const dashboardJson = (data: GetDashboardResult) =>
  http.get('*/api/dashboard', () => HttpResponse.json(data))

async function renderDashboard(handlers: RequestHandler[] = [dashboardJson(priced)]) {
  authStore.setUser({ id: 'u-1', email: 'holder@example.com' })

  server.use(
    ...handlers,
    dashboardSettingsHandler(),
    saveDashboardSettingsHandler,
    ...alertsHandlers,
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

const table = () => within(screen.getByRole('table'))

function healthCard(): HTMLElement {
  const section = screen.getByRole('heading', { name: 'API health' }).closest('section')
  expect(section).not.toBeNull()

  return section!
}

describe('the alerts panel when the cache is down', () => {
  it('says alerts are suppressed rather than sitting silently empty', async () => {
    await renderDashboard([dashboardJson(priced), healthDetailHandler({ redis: 'Unhealthy' })])

    expect(await screen.findByText(/alerts are suppressed/i)).toBeInTheDocument()
    expect(screen.getByText(/no threshold is being evaluated/i)).toBeInTheDocument()
  })

  it('says the same for a merely degraded cache', async () => {
    await renderDashboard([dashboardJson(priced), healthDetailHandler({ redis: 'Degraded' })])

    expect(await screen.findByText(/alerts are suppressed/i)).toBeInTheDocument()
  })

  it('says nothing of the sort while the cache is healthy', async () => {
    await renderDashboard()

    await screen.findByText(/nothing has crossed a threshold yet/i)
    await waitFor(() => expect(healthCard()).toHaveTextContent('Healthy'))

    expect(screen.queryByText(/alerts are suppressed/i)).not.toBeInTheDocument()
  })
})

describe('the health card', () => {
  it('renders one state per component group and no stubbed rows', async () => {
    await renderDashboard([
      dashboardJson(priced),
      healthDetailHandler({ databases: 'Healthy', redis: 'Degraded', feed: 'Unhealthy' }),
    ])

    await waitFor(() => expect(within(healthCard()).getByText('Degraded')).toBeInTheDocument())

    const card = within(healthCard())

    expect(card.getByText('Database')).toBeInTheDocument()
    expect(card.getByText('Healthy')).toBeInTheDocument()

    expect(card.getByText('Cache')).toBeInTheDocument()

    expect(card.getByText('Price feed')).toBeInTheDocument()
    expect(card.getByText('Down')).toBeInTheDocument()

    expect(card.getByText('FakeQuoteProvider')).toBeInTheDocument()

    expect(card.queryByText('Latency')).not.toBeInTheDocument()
    expect(card.queryByText('Quota used')).not.toBeInTheDocument()
    expect(card.queryByText('Phase 6')).not.toBeInTheDocument()
  })

  it('says so when the provider rejected the key', async () => {
    await renderDashboard([
      dashboardJson(priced),
      healthDetailHandler({ feed: 'Unhealthy', providerKeyRejected: true }),
    ])

    expect(await within(healthCard()).findByText(/rejected the API key/i)).toBeInTheDocument()
  })
})

describe('the degraded dashboard', () => {
  it('names the quote provider as the reason a price is trailing', async () => {
    await renderDashboard([dashboardJson(lastKnown)])

    expect(await screen.findByText(/the quote provider is not responding/i)).toBeInTheDocument()
  })

  it('marks a last-known price as one even with no timestamp', async () => {
    await renderDashboard([dashboardJson(lastKnown)])

    await waitFor(() => expect(table().getByText('AAPL')).toBeInTheDocument())

    expect(table().getByText(/last known/i)).toBeInTheDocument()
  })

  it('surfaces a failed refresh-interval save instead of only snapping the control back', async () => {
    const user = userEvent.setup()

    await renderDashboard([
      dashboardJson(priced),
      http.put('*/api/settings/dashboard', () =>
        HttpResponse.json(
          { title: 'Service unavailable', detail: 'The database is not reachable.', status: 503 },
          { status: 503, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    ])

    const control = screen.getByLabelText(/refresh/i)
    await waitFor(() => expect(control).toHaveValue('60'))

    await user.selectOptions(control, '15')

    expect(await screen.findByText(/the database is not reachable/i)).toBeInTheDocument()
    await waitFor(() => expect(control).toHaveValue('60'))
  })

  it('says prices are unavailable instead of showing a made-up zero', async () => {
    await renderDashboard([dashboardJson(nothingPriced)])

    expect(await screen.findByText(/prices are unavailable right now/i)).toBeInTheDocument()

    const row = await screen.findByRole('row', { name: /AAPL/ })

    expect(within(row).getAllByText('—')).toHaveLength(5)
    expect(within(row).queryAllByText(/^\D*0\.00$/)).toHaveLength(0)
  })
})
