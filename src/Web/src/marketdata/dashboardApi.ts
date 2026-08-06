import { apiFetch } from '../lib/apiClient'
import type { Money } from '../lib/format'

/**
 * The API contract, verbatim:
 *
 *   GET /api/dashboard              bearer      -> 200 GetDashboardResult
 *   GET /api/marketdata/health      anonymous   -> 200 { provider }
 *
 * Every nullable member below really arrives as `null` rather than being absent —
 * the server declares `JsonIgnore(Condition = Never)` on each one — so `null` means
 * "no price for this position", never "the field was omitted".
 */

export interface DashboardPosition {
  id: string
  ticker: string
  /** Company name from the name cache. `null` is normal — an uncached name, not a failure. */
  name: string | null
  quantity: number
  averagePrice: Money
  cost: Money
  currentPrice: Money | null
  marketValue: Money | null
  profit: Money | null
  /** Pre-formatted server-side, e.g. "20.00". The client appends the `%`. */
  profitPercent: string | null
  weight: string | null
  observedAt: string | null
  /** The price came out of the last-known store, not from a live fetch. */
  isLastKnown: boolean
}

export interface DashboardTotals {
  value: Money
  cost: Money
  profit: Money
  /** Null when nothing could be priced — "0.00" would claim an exactly break-even portfolio. */
  profitPercent: string | null
  positionCount: number
  /** Below `positionCount` when something could not be priced, which the footnote says. */
  pricedPositionCount: number
}

export interface GetDashboardResult {
  positions: DashboardPosition[]
  totals: DashboardTotals
  asOf: string
  /** `min(observedAt)` over priced positions; null when nothing is priced. */
  stalestObservedAt: string | null
}

export interface MarketDataHealth {
  provider: string
}

/** Query keys live beside the fetchers for their feature, exactly as `holdingKeys` does. */
export const dashboardKeys = {
  all: ['dashboard'] as const,
  view: () => [...dashboardKeys.all, 'view'] as const,
}

export const marketDataKeys = {
  all: ['marketdata'] as const,
  health: () => [...marketDataKeys.all, 'health'] as const,
}

export const fetchDashboard = (signal: AbortSignal): Promise<GetDashboardResult> =>
  apiFetch<GetDashboardResult>('/api/dashboard', { signal })

/** Anonymous, so it never triggers `apiFetch`'s refresh-and-retry on a signed-out tab. */
export const fetchMarketDataHealth = (signal: AbortSignal): Promise<MarketDataHealth> =>
  apiFetch<MarketDataHealth>('/api/marketdata/health', { signal, authenticated: false })
