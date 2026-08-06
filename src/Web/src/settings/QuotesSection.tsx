import { useId, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Alert } from '../components/Alert'
import { Card } from '../components/Card'
import { fallbackMessage, useSaveState, useSyncWhileIdle } from '../lib/useSaveState'
import { dashboardSettingsQuery, saveDashboardSettings, settingsKeys } from './settingsApi'
import { SaveButton } from './SaveButton'

/** The server's own range (`RefreshInterval.Minimum`/`Maximum`) — 10 to 300 seconds. */
const INTERVAL_OPTIONS = [15, 30, 60, 120, 300] as const

/** The sensible default for a stock dashboard, not a quota boundary — see `CLAUDE.md`. */
const DEFAULT_SECONDS = 60

/**
 * "Quotes" rather than "dashboard" because the cost this section explains — one provider
 * call per visible position, every refresh — is what the interval actually buys: how often
 * a price gets re-fetched, not a display preference.
 */
export function QuotesSection() {
  const { t } = useTranslation(['settings', 'common'])
  const { data } = useQuery(dashboardSettingsQuery)
  const queryClient = useQueryClient()
  const intervalId = useId()

  const [seconds, setSeconds] = useState(DEFAULT_SECONDS)
  const save = useSaveState()
  useSyncWhileIdle(data?.refreshIntervalSeconds, save.state, setSeconds)

  const mutation = useMutation({
    mutationFn: () => saveDashboardSettings({ refreshIntervalSeconds: seconds }),
    onSuccess: (result) => {
      queryClient.setQueryData(settingsKeys.dashboard(), result)
      save.succeed()
    },
    onError: (mutationError) => save.fail(fallbackMessage(mutationError, t('common:fallbackError'))),
  })

  return (
    <Card title={t('quotes.title')}>
      <div className="flex flex-col gap-3">
        <div className="flex flex-col gap-[7px]">
          <label htmlFor={intervalId} className="text-mu text-xs">
            {t('quotes.intervalLabel')}
          </label>
          <select
            id={intervalId}
            className="border-bd bg-panel text-tx rounded-[9px] border px-[13px] py-[11px] text-sm sm:max-w-[240px]"
            value={seconds}
            onChange={(event) => {
              setSeconds(Number(event.target.value))
              save.markDirty()
            }}
          >
            {INTERVAL_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {t(`quotes.intervalOptions.${option}`)}
              </option>
            ))}
          </select>
        </div>

        {/*
         * The honesty line: a call count, not a fraction of anybody's quota. D3 retired the
         * free-tier framing this screen used to carry — the app is not built on a free key.
         */}
        <p className="text-mu text-[11.5px] leading-relaxed">{t('quotes.costNote')}</p>

        <SaveButton
          state={save.state}
          onClick={() => {
            save.begin()
            mutation.mutate()
          }}
        />

        {save.state === 'error' ? <Alert tone="error">{save.error}</Alert> : null}
      </div>
    </Card>
  )
}
