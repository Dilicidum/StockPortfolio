import { Link } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Alert } from '../components/Alert'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import { formatAge, formatMoney, formatPercent } from '../lib/format'
import { alertHistoryQuery, alertKeys, simulateAlert, type FiredAlert } from './alertsApi'

interface AlertPanelProps {
  limit?: number
  compact?: boolean
}

function DirectionChip({ direction }: { direction: FiredAlert['direction'] }) {
  const { t } = useTranslation('alerts')
  const falling = direction === 'Fall'

  return (
    <span
      className={`rounded-full px-1.5 py-px text-[10.5px] font-medium tracking-[0.03em] uppercase ${
        falling ? 'bg-dn/12 text-dn' : 'bg-up/12 text-up'
      }`}
    >
      {falling ? t('direction.fall') : t('direction.rise')}
    </span>
  )
}

function AlertRow({ alert }: { alert: FiredAlert }) {
  const { t } = useTranslation('alerts')
  const falling = alert.direction === 'Fall'
  const firedAt = Date.parse(alert.firedAt)

  return (
    <li className="border-bd/60 flex flex-col gap-1.5 border-b py-3 last:border-0 first:pt-0">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-tx font-mono text-[13px]">{alert.ticker}</span>
        <DirectionChip direction={alert.direction} />

        {alert.isSimulated ? (
          <span className="border-bd text-mu rounded-full border px-1.5 py-px text-[10.5px] tracking-[0.03em] uppercase">
            {t('simulatedBadge')}
          </span>
        ) : null}

        <span className={`ml-auto font-mono text-[13px] ${falling ? 'text-dn' : 'text-up'}`}>
          {formatPercent(alert.changePercent)}
        </span>
      </div>

      <p className="text-mu text-[12px] leading-snug">{alert.reason}</p>

      <div className="text-mu flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1 text-[11.5px]">
        <span className="font-mono">
          {formatMoney(alert.triggerPrice)}
          <span className="text-mu/80">
            {t('panel.fromPrice', { price: formatMoney(alert.referencePrice) })}
          </span>
        </span>

        <time dateTime={alert.firedAt} title={alert.firedAt}>
          {Number.isNaN(firedAt)
            ? alert.firedAt
            : t('panel.agoLabel', { age: formatAge(Date.now() - firedAt) })}
        </time>
      </div>
    </li>
  )
}

export function AlertPanel({ limit, compact = false }: AlertPanelProps) {
  const { t } = useTranslation('alerts')
  const queryClient = useQueryClient()

  const { data, isPending, isError } = useQuery(alertHistoryQuery)

  const simulate = useMutation({
    mutationFn: () => simulateAlert(),
    onSettled: () => queryClient.invalidateQueries({ queryKey: alertKeys.history() }),
  })

  const alerts = data ?? []
  const shown = alerts.slice(0, limit)

  return (
    <Card
      title={t('panel.title')}
      action={
        compact ? (
          <Button
            variant="secondary"
            size="sm"
            onClick={() => simulate.mutate()}
            loading={simulate.isPending}
          >
            {t('panel.simulate')}
          </Button>
        ) : null
      }
    >
      {simulate.isError ? (
        <div className="mb-3">
          <Alert tone="warn">{t('panel.simulateFailure')}</Alert>
        </div>
      ) : null}

      {isError ? <Alert tone="info">{t('panel.loadFailure')}</Alert> : null}

      {shown.length === 0 && !isError ? (
        <p className="text-mu text-[12.5px]">{isPending ? t('panel.loading') : t('panel.empty')}</p>
      ) : (
        <ul aria-label={t('panel.listLabel')} className="flex flex-col">
          {shown.map((alert) => (
            <AlertRow key={alert.id} alert={alert} />
          ))}
        </ul>
      )}

      {compact && alerts.length > shown.length ? (
        <Link to="/notifications" className="text-ac mt-3 inline-block text-[12px] hover:underline">
          {t('panel.seeAll', { count: alerts.length })}
        </Link>
      ) : null}
    </Card>
  )
}
