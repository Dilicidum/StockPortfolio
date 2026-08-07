import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Alert } from './Alert'
import { Card } from './Card'
import { NO_VALUE } from '../lib/format'
import {
  componentStatus,
  databaseStatus,
  feedFacts,
  healthDetailQuery,
  FEED_COMPONENT,
  REDIS_COMPONENT,
  type HealthState,
} from '../health/healthApi'

const STATE_CLASS: Record<HealthState, string> = {
  Healthy: 'text-up',
  Degraded: 'text-warn',
  Unhealthy: 'text-dn',
}

interface RowProps {
  label: string
  value: string
  tone?: string
}

function Row({ label, value, tone }: RowProps) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <dt className="text-mu text-[12.5px]">{label}</dt>
      <dd className={`font-mono text-[12.5px] ${tone ?? ''}`}>{value}</dd>
    </div>
  )
}

interface StateRowProps {
  label: string
  state: HealthState | null
  unknownLabel: string
}

function StateRow({ label, state, unknownLabel }: StateRowProps) {
  const { t } = useTranslation('dashboard')

  if (!state) return <Row label={label} value={unknownLabel} tone="text-mu" />

  return <Row label={label} value={t(`apiHealth.state.${state}`)} tone={STATE_CLASS[state]} />
}

export function ApiHealth() {
  const { t } = useTranslation('dashboard')

  const { data, isError } = useQuery(healthDetailQuery)

  const facts = feedFacts(data)
  const unknownLabel = isError ? t('apiHealth.unreachable') : NO_VALUE

  return (
    <Card title={t('apiHealth.title')}>
      <dl className="flex flex-col gap-2">
        <StateRow label={t('apiHealth.database')} state={databaseStatus(data)} unknownLabel={unknownLabel} />
        <StateRow
          label={t('apiHealth.cache')}
          state={componentStatus(data, REDIS_COMPONENT)}
          unknownLabel={unknownLabel}
        />
        <StateRow
          label={t('apiHealth.feed')}
          state={componentStatus(data, FEED_COMPONENT)}
          unknownLabel={unknownLabel}
        />
        <Row label={t('apiHealth.quoteProvider')} value={facts.provider ?? unknownLabel} />
      </dl>

      {facts.providerKeyRejected ? (
        <div className="mt-3">
          <Alert tone="error">{t('apiHealth.keyRejected')}</Alert>
        </div>
      ) : null}
    </Card>
  )
}
