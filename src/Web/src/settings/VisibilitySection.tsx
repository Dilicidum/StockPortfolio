import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Alert } from '../components/Alert'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import { holdingsQuery, type Holding } from '../portfolio/holdingsApi'
import { useSetHoldingVisibility } from '../portfolio/useHoldingMutations'

/**
 * Requirement 8's "list of stocks" — a checkbox per position that controls whether it shows
 * on the dashboard. `useSetHoldingVisibility` already carries the optimistic
 * snapshot-and-rollback pattern `useHoldingMutations.ts` uses everywhere else, so a toggle
 * updates the counter immediately and un-does itself if the `PATCH` fails.
 */
export function VisibilitySection() {
  const { t } = useTranslation(['settings', 'common'])
  const { data } = useQuery(holdingsQuery)
  const holdings = data ?? []
  const setVisibility = useSetHoldingVisibility()
  const [error, setError] = useState('')

  const visibleCount = holdings.filter((holding) => holding.isVisible).length
  const hidden = holdings.filter((holding) => !holding.isVisible)

  function toggle(holding: Holding) {
    setError('')
    setVisibility.mutate(
      { id: holding.id, isVisible: !holding.isVisible },
      { onError: () => setError(t('visibility.toggleFailure', { ticker: holding.ticker })) },
    )
  }

  function showAll() {
    setError('')
    for (const holding of hidden) {
      setVisibility.mutate({ id: holding.id, isVisible: true })
    }
  }

  return (
    <Card
      title={t('visibility.title')}
      action={
        <div className="flex flex-wrap items-center gap-3">
          <span className="text-mu text-[12.5px]">
            {t('visibility.counter', { visible: visibleCount, total: holdings.length })}
          </span>
          {hidden.length > 0 ? (
            <Button variant="ghost" size="sm" onClick={showAll}>
              {t('visibility.showAll')}
            </Button>
          ) : null}
        </div>
      }
    >
      {error ? <Alert tone="error">{error}</Alert> : null}

      {holdings.length === 0 ? (
        <p className="text-mu text-[12.5px]">{t('visibility.empty')}</p>
      ) : (
        <ul className="flex flex-col gap-2.5">
          {holdings.map((holding) => (
            <li key={holding.id} className="flex items-center gap-2.5">
              <input
                id={`visibility-${holding.id}`}
                type="checkbox"
                className="accent-ac h-4 w-4"
                checked={holding.isVisible}
                onChange={() => toggle(holding)}
                aria-label={t('visibility.toggleAria', { ticker: holding.ticker })}
              />
              <label htmlFor={`visibility-${holding.id}`} className="text-tx text-[13px]">
                {holding.ticker}
                {holding.name ? <span className="text-mu"> — {holding.name}</span> : null}
              </label>
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}
