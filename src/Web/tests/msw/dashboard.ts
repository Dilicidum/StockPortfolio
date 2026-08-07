import { http, HttpResponse } from 'msw'
import type { GetDashboardResult } from '../../src/marketdata/dashboardApi'

const zero = { amount: '0', currency: 'USD' }

export const emptyDashboard: GetDashboardResult = {
  positions: [],
  totals: {
    value: zero,
    cost: zero,
    profit: zero,
    profitPercent: null,
    positionCount: 0,
    pricedPositionCount: 0,
  },
  asOf: '2026-08-05T12:00:05+00:00',
  stalestObservedAt: null,
}

export const dashboardHandler = http.get('*/api/dashboard', () => HttpResponse.json(emptyDashboard))

export const marketDataHealthHandler = http.get('*/api/marketdata/health', () =>
  HttpResponse.json({ provider: 'FakeQuoteProvider' }),
)

export const dashboardHandlers = [dashboardHandler, marketDataHealthHandler]
