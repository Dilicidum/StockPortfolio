import { createFileRoute } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
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
import { dashboardSettingsQuery, saveDashboardSettings, settingsKeys, type DashboardSettings } from '../../settings/settingsApi'

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
  { labelKey: 'refresh.every15s', value: 15_000 },
  { labelKey: 'refresh.every30s', value: 30_000 },
  { labelKey: 'refresh.every60s', value: 60_000 },
  { labelKey: 'refresh.every5m', value: 300_000 },
] as const

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
  const { t } = useTranslation('dashboard')
  const queryClient = useQueryClient()

  // Sourced from the settings screen's own query, not local state — a value changed here and
  // a value changed on /settings write through the SAME mutation below, so the two screens
  // can never disagree about how often a refresh actually happens.
  const { data: settings } = useQuery(dashboardSettingsQuery)
  const intervalMs = (settings?.refreshIntervalSeconds ?? DEFAULT_INTERVAL_MS / 1000) * 1000

  const saveInterval = useMutation({
    mutationFn: saveDashboardSettings,
    // Optimistic: the select is the only control here, so its own change has to show up
    // immediately rather than waiting on the round trip, exactly as the visibility toggle does.
    onMutate: (body) => {
      const previous = queryClient.getQueryData<DashboardSettings>(settingsKeys.dashboard())
      queryClient.setQueryData(settingsKeys.dashboard(), body)
      return { previous }
    },
    onSuccess: (result) => queryClient.setQueryData(settingsKeys.dashboard(), result),
    onError: (_error, _body, onMutateResult) => {
      if (onMutateResult?.previous) queryClient.setQueryData(settingsKeys.dashboard(), onMutateResult.previous)
    },
  })

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
    {
      header: t('columns.asset'),
      cell: (position) => <TickerCell ticker={position.ticker} name={position.name} />,
    },
    { header: t('columns.qty'), cell: (position) => position.quantity, numeric: true },
    { header: t('columns.buy'), cell: (position) => formatMoney(position.averagePrice), numeric: true },
    {
      header: t('columns.price'),
      numeric: true,
      // The per-row stamp §3 asks for: a single headline figure hides the one thinly
      // traded ticker that is minutes behind everything else on the page.
      cell: (position) => {
        if (!position.currentPrice) {
          return (
            <span className="text-mu" title={t('priceCell.awaitingPrice')}>
              {NO_VALUE}
            </span>
          )
        }

        const trailing = isTrailing(position, newestObservedAt, intervalMs)

        return (
          <span
            title={position.observedAt ? t('priceCell.observedAtTitle', { observedAt: position.observedAt }) : undefined}
          >
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
    { header: t('columns.value'), cell: (position) => formatMoney(position.marketValue), numeric: true },
    {
      header: t('columns.pl'),
      numeric: true,
      cell: (position) => (
        <span className={position.profit ? (isNegative(position.profit) ? 'text-dn' : 'text-up') : 'text-mu'}>
          {formatMoney(position.profit)}
        </span>
      ),
    },
    {
      header: t('columns.plPercent'),
      numeric: true,
      cell: (position) => (
        <span className={position.profit ? (isNegative(position.profit) ? 'text-dn' : 'text-up') : 'text-mu'}>
          {formatPercent(position.profitPercent)}
        </span>
      ),
    },
    { header: t('columns.weight'), cell: (position) => formatPercent(position.weight), numeric: true },
  ]

  return (
    <AppShell title={t('title')} subtitle={user ? t('subtitleSignedIn', { email: user.email }) : undefined}>
      {isError ? (
        <Alert tone="error" title={t('error.title')}>
          {error instanceof Error && error.message ? error.message : t('error.fallback')}
          {data ? t('error.showingLastKnown') : ''}
        </Alert>
      ) : null}

      <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-2 xl:grid-cols-4">
        <StatTile label={t('stats.totalValue')} value={formatMoney(totals?.value)} />
        <StatTile label={t('stats.invested')} value={formatMoney(totals?.cost)} />
        <StatTile
          label={t('stats.unrealisedPl')}
          value={formatMoney(totals?.profit)}
          hint={totals ? formatPercent(totals.profitPercent) : undefined}
          tone={toneOf(totals?.profit)}
        />
        <StatTile
          label={t('stats.positions')}
          value={totals ? totals.positionCount : NO_VALUE}
          hint={totals ? t('stats.pricedHint', { count: totals.pricedPositionCount }) : undefined}
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
            title={t('holdings.cardTitle')}
            action={
              <div className="flex flex-wrap items-center justify-end gap-3">
                <Freshness
                  asOf={data?.asOf}
                  stalestObservedAt={data?.stalestObservedAt}
                  // Two cycles: one missed refresh is a blip, two is a story worth telling.
                  staleAfterMs={intervalMs * 2}
                />
                <label className="text-mu flex items-center gap-2 text-[12.5px]">
                  {t('holdings.refreshLabel')}
                  <select
                    className="border-bd bg-panel-2 text-tx rounded-lg border px-2 py-1 text-[12.5px]"
                    value={intervalMs}
                    onChange={(event) =>
                      saveInterval.mutate({ refreshIntervalSeconds: Number(event.target.value) / 1000 })
                    }
                  >
                    {INTERVALS.map((option) => (
                      <option key={option.value} value={option.value}>
                        {t(option.labelKey)}
                      </option>
                    ))}
                  </select>
                </label>
              </div>
            }
          >
            <Table
              caption={t('holdings.caption')}
              columns={columns}
              rows={positions}
              rowKey={(position) => position.id}
              empty={isPending ? t('holdings.loading') : t('holdings.empty')}
            />

            {unpriced > 0 ? (
              <p className="text-mu mt-3 text-[11.5px]">
                {t('holdings.unpricedNote', { unpriced, total: totals?.positionCount })}
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
