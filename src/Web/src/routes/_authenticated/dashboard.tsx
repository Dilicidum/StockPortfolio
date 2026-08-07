import { createFileRoute, useRouter, type ErrorComponentProps } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { AlertPanel } from '../../alerts/AlertPanel'
import { PANEL_ROWS } from '../../alerts/alertsApi'
import { Alert } from '../../components/Alert'
import { ApiHealth } from '../../components/ApiHealth'
import { AppShell } from '../../components/AppShell'
import { Button } from '../../components/Button'
import { Card } from '../../components/Card'
import { ErrorBoundary } from '../../components/ErrorBoundary'
import { Freshness } from '../../components/Freshness'
import { StatTile } from '../../components/StatTile'
import { Table, type Column } from '../../components/Table'
import { TickerCell } from '../../components/TickerCell'
import { useAuth } from '../../auth/useAuth'
import { formatAge, formatMoney, formatPercent, isNegative, NO_VALUE, type Money } from '../../lib/format'
import { dashboardKeys, fetchDashboard, type DashboardPosition } from '../../marketdata/dashboardApi'
import { fallbackMessage, serverMessage } from '../../lib/formErrors'
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
  errorComponent: DashboardError,
})

function DashboardError({ error }: ErrorComponentProps) {
  const router = useRouter()
  const { t } = useTranslation(['dashboard', 'common'])

  return (
    <AppShell title={t('title')}>
      <Alert tone="error" title={t('routeError.title')}>
        {error.message || t('routeError.fallback')}
      </Alert>

      <div className="sm:max-w-[200px]">
        <Button onClick={() => void router.invalidate()}>{t('common:actions.tryAgain')}</Button>
      </div>
    </AppShell>
  )
}

const TONE_CLASS = { neutral: 'text-mu', up: 'text-up', down: 'text-dn' } as const

function toneOf(money: Money | null | undefined): 'neutral' | 'up' | 'down' {
  if (!money) return 'neutral'
  return isNegative(money) ? 'down' : 'up'
}

function isLate(position: DashboardPosition, newestObservedAt: number, staleAfterMs: number): boolean {
  if (!position.observedAt) return false

  return newestObservedAt - Date.parse(position.observedAt) > staleAfterMs
}

function DashboardPage() {
  const { user } = useAuth()
  const { t } = useTranslation(['dashboard', 'alerts', 'common'])
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
  const nothingPriced = !!totals && totals.positionCount > 0 && totals.pricedPositionCount === 0

  const stalestObservedAt = data?.stalestObservedAt
  const providerNotResponding =
    positions.some((position) => position.isLastKnown) ||
    nothingPriced ||
    (!!stalestObservedAt && Date.now() - Date.parse(stalestObservedAt) > intervalMs * 2)

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

        const age = position.observedAt ? formatAge(Date.now() - Date.parse(position.observedAt)) : null
        const late = isLate(position, newestObservedAt, intervalMs)

        return (
          <span
            title={position.observedAt ? t('priceCell.observedAtTitle', { observedAt: position.observedAt }) : undefined}
          >
            {formatMoney(position.currentPrice)}

            {position.isLastKnown ? (
              <span className="text-warn ml-1.5 text-[11.5px]" title={t('priceCell.lastKnownTitle')}>
                {age ? t('priceCell.lastKnownWithAge', { age }) : t('priceCell.lastKnown')}
              </span>
            ) : late && age ? (
              <span className="text-warn ml-1.5 text-[11.5px]">{age}</span>
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
      ) : providerNotResponding ? (
        <Alert tone="warn" title={t('stale.title')}>
          {t('stale.reason')}
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
            {saveInterval.isError ? (
              <div className="mb-3">
                <Alert tone="error">
                  {serverMessage(saveInterval.error, t('holdings.refreshSaveFailure'))}
                </Alert>
              </div>
            ) : null}

            <Table
              caption={t('holdings.caption')}
              columns={columns}
              rows={positions}
              rowKey={(position) => position.id}
              empty={isPending ? t('holdings.loading') : t('holdings.empty')}
            />

            {nothingPriced ? (
              <p className="text-warn mt-3 text-[11.5px]">{t('holdings.pricesUnavailable')}</p>
            ) : unpriced > 0 ? (
              <p className="text-mu mt-3 text-[11.5px]">
                {t('holdings.unpricedNote', { unpriced, total: totals?.positionCount })}
              </p>
            ) : null}
          </Card>

          <ApiHealth />
        </div>

        <ErrorBoundary
          fallback={({ retry }) => (
            <Card title={t('alerts:panel.title')}>
              <Alert tone="error" title={t('alerts:panel.crashTitle')}>
                {t('alerts:panel.crashBody')}
              </Alert>

              <div className="mt-3 sm:max-w-[200px]">
                <Button variant="secondary" size="sm" onClick={retry}>
                  {t('common:actions.tryAgain')}
                </Button>
              </div>
            </Card>
          )}
        >
          <AlertPanel limit={PANEL_ROWS} compact />
        </ErrorBoundary>
      </div>
    </AppShell>
  )
}
