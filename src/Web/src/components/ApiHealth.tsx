import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Card } from './Card'
import { NO_VALUE } from '../lib/format'
import { fetchMarketDataHealth, marketDataKeys } from '../marketdata/dashboardApi'

interface RowProps {
  label: string
  value: string
  note?: string
}

function Row({ label, value, note }: RowProps) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <dt className="text-mu text-[12.5px]">{label}</dt>
      <dd className="flex items-baseline gap-2">
        <span className="font-mono text-[12.5px]">{value}</span>
        {note ? <span className="text-mu text-[11.5px]">{note}</span> : null}
      </dd>
    </div>
  )
}

/**
 * The provider name comes from the API rather than from a build flag, so the string in
 * the startup log and the string on this page cannot drift. Latency and quota are the
 * Phase 6 half of the panel and are labelled as such rather than left blank.
 */
export function ApiHealth() {
  const { t } = useTranslation('dashboard')

  const { data, isError } = useQuery({
    queryKey: marketDataKeys.health(),
    queryFn: ({ signal }) => fetchMarketDataHealth(signal),
    staleTime: 300_000,
  })

  const provider = data?.provider ?? (isError ? t('apiHealth.unreachable') : NO_VALUE)

  return (
    <Card title={t('apiHealth.title')}>
      <dl className="flex flex-col gap-2">
        <Row label={t('apiHealth.quoteProvider')} value={provider} />
        <Row label={t('apiHealth.latency')} value={NO_VALUE} note={t('apiHealth.futurePhaseNote')} />
        <Row label={t('apiHealth.quotaUsed')} value={NO_VALUE} note={t('apiHealth.futurePhaseNote')} />
      </dl>
    </Card>
  )
}
