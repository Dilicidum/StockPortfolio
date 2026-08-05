import { http, HttpResponse } from 'msw'
import type { GetDashboardResult } from '../../src/marketdata/dashboardApi'

const zero = { amount: '0', currency: 'USD' }

/** A signed-in account with nothing in it — the shape every route-mounting test needs. */
export const emptyDashboard: GetDashboardResult = {
  positions: [],
  totals: {
    value: zero,
    cost: zero,
    profit: zero,
    // Null, matching the server: with no priced position there is no cost to divide by.
    profitPercent: null,
    positionCount: 0,
    pricedPositionCount: 0,
  },
  asOf: '2026-08-05T12:00:05+00:00',
  stalestObservedAt: null,
}

/**
 * `/dashboard` fetches on mount, and `tests/setup.ts` runs MSW with
 * `onUnhandledRequest: 'error'` over a server with no default handlers — so every test
 * that merely mounts the route needs these, not only the dashboard's own tests.
 */
export const dashboardHandler = http.get('*/api/dashboard', () => HttpResponse.json(emptyDashboard))

export const marketDataHealthHandler = http.get('*/api/marketdata/health', () =>
  HttpResponse.json({ provider: 'FakeQuoteProvider' }),
)

export const dashboardHandlers = [dashboardHandler, marketDataHealthHandler]
