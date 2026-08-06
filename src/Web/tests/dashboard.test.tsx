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

/**
 * AAPL is priced, TSLA is not, and `totals` is DELIBERATELY not the sum of the two rows —
 * 4242.42 cannot be arrived at from anything on screen, so a test asserting it can only
 * pass if the figure came off the wire.
 */
const dashboard: GetDashboardResult = {
  positions: [
    {
      id: '0199a1f0-0000-7000-8000-000000000001',
      ticker: 'AAPL',
      // A name the cache knew; TSLA's is null, which is the ordinary case for a position
      // recorded before ticker search existed. Both render through `TickerCell`.
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

/** `asOf` and the prices are minutes old in wall-clock terms unless a test says otherwise. */
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

// The QueryClient is a module singleton shared by every test FILE in the run, so a
// seeded dashboard leaks into the next file unless it is cleared here.
beforeEach(() => {
  authStore.signOut()
  queryClient.clear()
  __resetRefreshInFlight()
})

const dashboardJson = (data: GetDashboardResult) =>
  http.get('*/api/dashboard', () => HttpResponse.json(data))

/** The fourth inline copy of the memory-router boilerplate, which is the convention here. */
async function renderDashboard(handlers: RequestHandler[] = [dashboardJson(freshCopy())]) {
  authStore.setUser({ id: 'u-1', email: 'holder@example.com' })
  // The alert panel and the layout's stream fetch on mount too, and the refresh interval
  // now reads and writes `/api/settings/dashboard` (see dashboard.tsx) — MSW errors on
  // anything unhandled, so a dashboard mount needs all three stubs, not only its own.
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

/**
 * `Table` renders the desktop table AND the mobile card list into the DOM at every width —
 * CSS `display:none` picks one and jsdom applies no CSS. An unscoped `getByText('AAPL')`
 * therefore finds two nodes and throws, so every row query goes through here.
 */
const row = () => within(screen.getByRole('table'))

/** The wired option, read off the observer — a DOM-only assertion would not prove it reached the query. */
const refetchInterval = () =>
  queryClient.getQueryCache().find({ queryKey: dashboardKeys.view() })?.observers[0]?.options
    .refetchInterval

describe('dashboard', () => {
  it('renders totals from the API without client-side arithmetic', async () => {
    await renderDashboard()

    // Matched on the digits, not on "US$4,242.42": the runner's locale is en-GB, which
    // renders USD with a "US$" prefix. The separator is loose for the same reason.
    expect(await screen.findByText(/4.?242\.42/)).toBeInTheDocument()

    // Rows sum to $3,000 and the server says $4,242.42. Only the server's figure is shown,
    // which is the whole assertion — a browser-side reduce could never produce it.
    expect(screen.getByText(/21\.21%/)).toBeInTheDocument()

    await waitFor(() => expect(row().getByText('AAPL')).toBeInTheDocument())
  })

  it('renders a null price as pending, not $0.00', async () => {
    await renderDashboard()

    const tsla = await screen.findByRole('row', { name: /TSLA/ })

    // Price, Value, P/L, P/L % and Weight are all absent for an unpriced position;
    // quantity, buy price and cost are not.
    expect(within(tsla).getAllByText('—')).toHaveLength(5)

    // The anchored regex is what makes this real: it matches a cell whose whole text is
    // a zero amount ("US$0.00") and NOT the row's genuine "US$1,000.00" cost.
    expect(within(tsla).queryAllByText(/^\D*0\.00$/)).toHaveLength(0)
  })

  /*
   * The totals row makes the same claim a row's weight does. `profitPercent` is null when
   * nothing could be priced, and "0.00%" there would tell the holder their portfolio is
   * exactly break-even at the moment nothing about it is known.
   */
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

    // Scoped to the one tile that carries the figure: '—' appears all over an unpriced table.
    const tile = (await screen.findByText('Unrealised P&L')).parentElement
    expect(tile).not.toBeNull()

    expect(within(tile!).getByText('—')).toBeInTheDocument()
    expect(within(tile!).queryByText(/0\.00\s*%/)).not.toBeInTheDocument()
  })

  it('shows the amber freshness state when the prices behind the figures are stale', async () => {
    const stale: GetDashboardResult = {
      ...dashboard,
      asOf: new Date().toISOString(),
      // Half an hour behind a 60s refresh interval: two cycles is the threshold.
      stalestObservedAt: new Date(Date.now() - 1_800_000).toISOString(),
    }

    await renderDashboard([dashboardJson(stale)])

    const freshness = await screen.findByText(/prices up to \d+m old/i)
    expect(freshness).toHaveClass('text-warn')
  })

  it('changes refetchInterval when the interval control changes', async () => {
    const user = userEvent.setup()
    await renderDashboard()

    // The documented default, and not free to change: §3's free-tier arithmetic assumes it.
    await waitFor(() => expect(refetchInterval()).toBe(60_000))

    await user.selectOptions(screen.getByLabelText(/refresh/i), '15000')

    await waitFor(() => expect(refetchInterval()).toBe(15_000))
  })

  /*
   * The degradation the brief grades. With no `loader` and no `errorComponent`, a failed
   * refresh has to leave the table standing and put the reason above it.
   *
   * 404 rather than 500 on purpose: `queryClient`'s retry predicate stops at 4xx but
   * retries 5xx twice with exponential backoff, which would make this a 3-second test for
   * no extra coverage. The status is not what is under test; the retained table is.
   */
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

    // Still priced, still on screen — the failure replaced nothing.
    expect(row().getByText('AAPL')).toBeInTheDocument()
    expect(screen.getByText(/4.?242\.42/)).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Main' })).toBeInTheDocument()
  })
})
