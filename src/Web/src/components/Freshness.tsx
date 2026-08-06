import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { formatAge } from '../lib/format'

export interface FreshnessProps {
  /** When the server answered. */
  asOf: string | null | undefined
  /** Oldest price behind the figures on screen; null when nothing is priced. */
  stalestObservedAt: string | null | undefined
  /** Past this age the line turns amber. */
  staleAfterMs: number
}

/**
 * "Updated 12s ago", and amber once the oldest price behind the numbers is older than
 * the refresh interval allows for — a global figure alone hides a thinly traded ticker
 * that is minutes behind the rest.
 */
export function Freshness({ asOf, stalestObservedAt, staleAfterMs }: FreshnessProps) {
  const { t } = useTranslation('dashboard')
  const [now, setNow] = useState(() => Date.now())

  // React 19 StrictMode double-invokes effects, so the clear is what stops a second
  // timer from surviving the remount and ticking twice per period forever.
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
