import { useState } from 'react'
import { createFileRoute } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { AlertPanel } from '../../alerts/AlertPanel'
import { PANEL_ROWS } from '../../alerts/alertsApi'
import { Alert } from '../../components/Alert'
import { ApiHealth } from '../../components/ApiHealth'
import { AppShell } from '../../components/AppShell'
import { Card } from '../../components/Card'
import { Freshness } from '../../components/Freshness'
import { StatTile } from '../../components/StatTile'
import { Table, type Column } from '../../components/Table'
import { TickerCell } from '../../components/TickerCell'
import { useAuth } from '../../auth/useAuth'
import { formatAge, formatMoney, formatPercent, isNegative, NO_VALUE, type Money } from '../../lib/format'
import { dashboardKeys, fetchDashboard, type DashboardPosition } from '../../marketdata/dashboardApi'

/**
 * NO LOADER AND NO ERROR COMPONENT, unlike `portfolio.tsx` — whose own comment says so.
 *
 * Holdings are the page and are worth waiting for; quotes are not. A loader failure
 * takes the whole route down with an error component, and the brief grades visible
 * degraded state rather than a blank screen. Plain `useQuery` keeps the last good table
 * on screen and puts the failure in a banner above it.
 */
export const Route = createFileRoute('/_authenticated/dashboard')({
  component: DashboardPage,
})

/**
 * 60s is not free to change: §3's free-tier arithmetic — twenty of sixty calls a minute
 * for one viewer — assumes it, and 15s quadruples that figure for anyone who picks it.
 */
const INTERVALS = [
  { label: 'every 15s', value: 15_000 },
  { label: 'every 30s', value: 30_000 },
  { label: 'every 60s', value: 60_000 },
  { label: 'every 5m', value: 300_000 },
]

const DEFAULT_INTERVAL_MS = 60_000

function toneOf(money: Money | null | undefined): 'neutral' | 'up' | 'down' {
  if (!money) return 'neutral'
  return isNegative(money) ? 'down' : 'up'
}

/** A row worth stamping individually: served from the last-known store, or well behind its peers. */
function isTrailing(position: DashboardPosition, newestObservedAt: number, staleAfterMs: number): boolean {
  if (position.isLastKnown) return true
  if (!position.observedAt) return false

  return newestObservedAt - Date.parse(position.observedAt) > staleAfterMs
}

function DashboardPage() {
  const { user } = useAuth()
  const [intervalMs, setIntervalMs] = useState(DEFAULT_INTERVAL_MS)

  // The app's first `useQuery` — every other query is a `useSuspenseQuery` behind a
  // loader. All three options below override a global default deliberately.
  const { data, isPending, isError, error } = useQuery({
    queryKey: dashboardKeys.view(),
    queryFn: ({ signal }) => fetchDashboard(signal),
    refetchInterval: intervalMs,
    refetchOnWindowFocus: true,
    staleTime: 0,
  })

  const positions = data?.positions ?? []
  const totals = data?.totals

  const newestObservedAt = Math.max(
    0,
    ...positions.map((position) => (position.observedAt ? Date.parse(position.observedAt) : 0)),
  )

  const unpriced = totals ? totals.positionCount - totals.pricedPositionCount : 0

  const columns: Array<Column<DashboardPosition>> = [
    { header: 'Asset', cell: (position) => <TickerCell ticker={position.ticker} name={position.name} /> },
    { header: 'Qty', cell: (position) => position.quantity, numeric: true },
    { header: 'Buy', cell: (position) => formatMoney(position.averagePrice), numeric: true },
    {
      header: 'Price',
      numeric: true,
      // The per-row stamp §3 asks for: a single headline figure hides the one thinly
      // traded ticker that is minutes behind everything else on the page.
      cell: (position) => {
        if (!position.currentPrice) {
          return (
            <span className="text-mu" title="Awaiting a price for this position">
              {NO_VALUE}
            </span>
          )
        }

        const trailing = isTrailing(position, newestObservedAt, intervalMs)

        return (
          <span title={position.observedAt ? `Observed at ${position.observedAt}` : undefined}>
            {formatMoney(position.currentPrice)}
            {trailing && position.observedAt ? (
              <span className="text-warn ml-1.5 text-[11.5px]">
                {formatAge(Date.now() - Date.parse(position.observedAt))}
              </span>
            ) : null}
          </span>
        )
      },
    },
    { header: 'Value', cell: (position) => formatMoney(position.marketValue), numeric: true },
    {
      header: 'P/L',
      numeric: true,
      cell: (position) => (
        <span className={position.profit ? (isNegative(position.profit) ? 'text-dn' : 'text-up') : 'text-mu'}>
          {formatMoney(position.profit)}
        </span>
      ),
    },
    {
      header: 'P/L %',
      numeric: true,
      cell: (position) => (
        <span className={position.profit ? (isNegative(position.profit) ? 'text-dn' : 'text-up') : 'text-mu'}>
          {formatPercent(position.profitPercent)}
        </span>
      ),
    },
    { header: 'Weight', cell: (position) => formatPercent(position.weight), numeric: true },
  ]

  return (
    <AppShell title="Dashboard" subtitle={user ? `Signed in as ${user.email}` : undefined}>
      {isError ? (
        <Alert tone="error" title="Could not refresh prices">
          {error instanceof Error && error.message ? error.message : 'The server did not answer.'}
          {data ? ' Showing the last figures that arrived.' : ''}
        </Alert>
      ) : null}

      <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-2 xl:grid-cols-4">
        <StatTile label="Total value" value={formatMoney(totals?.value)} />
        <StatTile label="Invested" value={formatMoney(totals?.cost)} />
        <StatTile
          label="Unrealised P&L"
          value={formatMoney(totals?.profit)}
          hint={totals ? formatPercent(totals.profitPercent) : undefined}
          tone={toneOf(totals?.profit)}
        />
        <StatTile
          label="Positions"
          value={totals ? totals.positionCount : NO_VALUE}
          hint={totals ? `${totals.pricedPositionCount} priced` : undefined}
        />
      </div>

      {/*
       * The mockup's right-hand column, and it stacks under the table below `xl` — at 375px
       * the panel is a full-width list of rows, which is the layout it was designed as.
       * `min-w-0` on the left column is what stops a wide table from pushing the panel off
       * the grid instead of scrolling inside its own cell.
       */}
      <div className="grid grid-cols-1 gap-3.5 xl:grid-cols-[minmax(0,1fr)_minmax(0,330px)]">
        <div className="flex min-w-0 flex-col gap-3.5">
          <Card
            title="Holdings"
            action={
              <div className="flex flex-wrap items-center justify-end gap-3">
                <Freshness
                  asOf={data?.asOf}
                  stalestObservedAt={data?.stalestObservedAt}
                  // Two cycles: one missed refresh is a blip, two is a story worth telling.
                  staleAfterMs={intervalMs * 2}
                />
                <label className="text-mu flex items-center gap-2 text-[12.5px]">
                  Refresh
                  <select
                    className="border-bd bg-panel-2 text-tx rounded-lg border px-2 py-1 text-[12.5px]"
                    value={intervalMs}
                    onChange={(event) => setIntervalMs(Number(event.target.value))}
                  >
                    {INTERVALS.map((option) => (
                      <option key={option.value} value={option.value}>
                        {option.label}
                      </option>
                    ))}
                  </select>
                </label>
              </div>
            }
          >
            <Table
              caption="Your positions, priced"
              columns={columns}
              rows={positions}
              rowKey={(position) => position.id}
              empty={isPending ? 'Fetching prices…' : 'No positions yet. Add one on the Portfolio page.'}
            />

            {unpriced > 0 ? (
              <p className="text-mu mt-3 text-[11.5px]">
                {unpriced} of {totals?.positionCount} positions have no price yet and are excluded
                from the totals above.
              </p>
            ) : null}
          </Card>

          <ApiHealth />
        </div>

        <AlertPanel limit={PANEL_ROWS} compact />
      </div>
    </AppShell>
  )
}
