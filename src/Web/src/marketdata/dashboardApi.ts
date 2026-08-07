import { apiFetch } from '../lib/apiClient'
import type { Money } from '../lib/format'

export interface DashboardPosition {
  id: string
  ticker: string
  name: string | null
  quantity: number
  averagePrice: Money
  cost: Money
  currentPrice: Money | null
  marketValue: Money | null
  profit: Money | null
  profitPercent: string | null
  weight: string | null
  observedAt: string | null
  isLastKnown: boolean
}

export interface DashboardTotals {
  value: Money
  cost: Money
  profit: Money
  profitPercent: string | null
  positionCount: number
  pricedPositionCount: number
}

export interface GetDashboardResult {
  positions: DashboardPosition[]
  totals: DashboardTotals
  asOf: string
  stalestObservedAt: string | null
}

export const dashboardKeys = {
  all: ['dashboard'] as const,
  view: () => [...dashboardKeys.all, 'view'] as const,
}

export const fetchDashboard = (signal: AbortSignal): Promise<GetDashboardResult> =>
  apiFetch<GetDashboardResult>('/api/dashboard', { signal })
