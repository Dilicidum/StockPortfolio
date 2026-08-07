import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Alert } from '../components/Alert'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import { serverMessage } from '../lib/formErrors'
import { dashboardKeys } from '../marketdata/dashboardApi'
import { holdingKeys, holdingsQuery, setHoldingVisibility, type Holding } from '../portfolio/holdingsApi'
import { useSetHoldingVisibility } from '../portfolio/useHoldingMutations'

export function VisibilitySection() {
  const { t } = useTranslation(['settings', 'common'])
  const { data } = useQuery(holdingsQuery)
  const queryClient = useQueryClient()
  const holdings = data ?? []
  const setVisibility = useSetHoldingVisibility()
  const [error, setError] = useState('')

  const visibleCount = holdings.filter((holding) => holding.isVisible).length
  const hidden = holdings.filter((holding) => !holding.isVisible)

  function toggle(holding: Holding) {
    setError('')
    setVisibility.mutate(
      { id: holding.id, isVisible: !holding.isVisible },
      {
        onError: (reason) =>
          setError(serverMessage(reason, t('visibility.toggleFailure', { ticker: holding.ticker }))),
      },
    )
  }

  async function showAll() {
    setError('')
    const targets = hidden
    if (targets.length === 0) return

    const targetIds = new Set(targets.map((holding) => holding.id))
    const previous = queryClient.getQueryData<Holding[]>(holdingKeys.list())

    queryClient.setQueryData<Holding[]>(holdingKeys.list(), (old) =>
      (old ?? []).map((holding) => (targetIds.has(holding.id) ? { ...holding, isVisible: true } : holding)),
    )

    const outcomes = await Promise.allSettled(targets.map((holding) => setHoldingVisibility(holding.id, true)))
    const failed = targets.filter((_holding, index) => outcomes[index]?.status === 'rejected')

    if (failed.length > 0) {
      const failedIds = new Set(failed.map((holding) => holding.id))
      queryClient.setQueryData<Holding[]>(holdingKeys.list(), (old) =>
        (old ?? []).map((holding) =>
          failedIds.has(holding.id)
            ? { ...holding, isVisible: previous?.find((p) => p.id === holding.id)?.isVisible ?? false }
            : holding,
        ),
      )
      const refusal = outcomes.find((outcome) => outcome.status === 'rejected')?.reason
      const named = t('visibility.showAllFailure', {
        tickers: failed.map((holding) => holding.ticker).join(', '),
      })

      setError(`${named} ${serverMessage(refusal, '')}`.trim())
    }

    queryClient.invalidateQueries({ queryKey: dashboardKeys.view() })
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
