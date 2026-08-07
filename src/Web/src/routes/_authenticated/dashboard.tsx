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
import { fallbackMessage } from '../../lib/formErrors'
import {
  DEFAULT_REFRESH_SECONDS,
  REFRESH_INTERVAL_SECONDS,
  dashboardSettingsQuery,
  saveDashboardSettings,
  settingsKeys,
  type DashboardSettings,
} from '../../settings/settingsApi'

export const Route = createFileRoute('/_authenticated/dashboard')({
  component: DashboardPage,
})

const TONE_CLASS = { neutral: 'text-mu', up: 'text-up', down: 'text-dn' } as const

function toneOf(money: Money | null | undefined): 'neutral' | 'up' | 'down' {
  if (!money) return 'neutral'
  return isNegative(money) ? 'down' : 'up'
}

function isTrailing(position: DashboardPosition, newestObservedAt: number, staleAfterMs: number): boolean {
  if (position.isLastKnown) return true
  if (!position.observedAt) return false

  return newestObservedAt - Date.parse(position.observedAt) > staleAfterMs
}

function DashboardPage() {
  const { user } = useAuth()
  const { t } = useTranslation('dashboard')
  const queryClient = useQueryClient()

  const { data: settings } = useQuery(dashboardSettingsQuery)
  const intervalSeconds = settings?.refreshIntervalSeconds ?? DEFAULT_REFRESH_SECONDS
  const intervalMs = intervalSeconds * 1000

  const saveInterval = useMutation({
    mutationFn: saveDashboardSettings,
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
        <span className={TONE_CLASS[toneOf(position.profit)]}>
          {formatMoney(position.profit)}
        </span>
      ),
    },
    {
      header: t('columns.plPercent'),
      numeric: true,
      cell: (position) => (
        <span className={TONE_CLASS[toneOf(position.profit)]}>
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
          {fallbackMessage(error, t('error.fallback'))}
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

      <div className="grid grid-cols-1 gap-3.5 xl:grid-cols-[minmax(0,1fr)_minmax(0,330px)]">
        <div className="flex min-w-0 flex-col gap-3.5">
          <Card
            title={t('holdings.cardTitle')}
            action={
              <div className="flex flex-wrap items-center justify-end gap-3">
                <Freshness
                  asOf={data?.asOf}
                  stalestObservedAt={data?.stalestObservedAt}
                  staleAfterMs={intervalMs * 2}
                />
                <label className="text-mu flex items-center gap-2 text-[12.5px]">
                  {t('holdings.refreshLabel')}
                  <select
                    className="border-bd bg-panel-2 text-tx rounded-lg border px-2 py-1 text-[12.5px]"
                    value={intervalSeconds}
                    onChange={(event) =>
                      saveInterval.mutate({ refreshIntervalSeconds: Number(event.target.value) })
                    }
                  >
                    {REFRESH_INTERVAL_SECONDS.map((seconds) => (
                      <option key={seconds} value={seconds}>
                        {t(`refresh.${seconds}`)}
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
