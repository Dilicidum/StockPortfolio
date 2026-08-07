import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { formatAge } from '../lib/format'

export interface FreshnessProps {
  asOf: string | null | undefined
  stalestObservedAt: string | null | undefined
  staleAfterMs: number
}

export function Freshness({ asOf, stalestObservedAt, staleAfterMs }: FreshnessProps) {
  const { t } = useTranslation('dashboard')
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 10_000)
    return () => clearInterval(timer)
  }, [])

  if (!asOf) {
    return <span className="text-mu text-[12.5px]">{t('freshness.waiting')}</span>
  }

  const priceAge = stalestObservedAt ? now - Date.parse(stalestObservedAt) : null
  const stale = priceAge !== null && priceAge > staleAfterMs

  return (
    <span
      role="status"
      className={`text-[12.5px] ${stale ? 'text-warn' : 'text-mu'}`}
      title={stalestObservedAt ? t('freshness.oldestPriceTitle', { stalestObservedAt }) : undefined}
    >
      {stale
        ? t('freshness.staleLabel', { age: formatAge(priceAge) })
        : t('freshness.updatedLabel', { age: formatAge(now - Date.parse(asOf)) })}
    </span>
  )
}
