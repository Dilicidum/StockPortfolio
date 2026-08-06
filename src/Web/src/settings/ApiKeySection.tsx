import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Alert } from '../components/Alert'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import { TextField } from '../components/TextField'
import { ApiError } from '../lib/apiClient'
import { apiKeyStatusQuery, removeApiKey, saveApiKey, settingsKeys, type ApiKeyStatus } from './settingsApi'

type SaveState = 'idle' | 'saving' | 'saved' | 'error'
/** 400 and 503 stay different sentences — a refused key is not the same failure as a provider
 * that merely could not answer, and conflating them is the `c: 0` mistake all over again. */
function messageFor(error: unknown, t: (key: string) => string): string {
  if (error instanceof ApiError) {
    if (error.status === 400) return t('apiKey.rejectedMessage')
    if (error.status === 503) return t('apiKey.unavailableMessage')
    if (error.status === 404) return t('apiKey.disabledMessage')
  }
  return error instanceof Error && error.message ? error.message : t('common:fallbackError')
}
export function ApiKeySection() {
  const { t } = useTranslation(['settings', 'common'])
  const { data } = useQuery(apiKeyStatusQuery)
  const queryClient = useQueryClient()
  const [apiKey, setApiKey] = useState('')
  const [state, setState] = useState<SaveState>('idle')
  const [error, setError] = useState('')
  function fail(mutationError: unknown) {
    setState('error')
    setError(messageFor(mutationError, t))
  }

  const save = useMutation({
    mutationFn: () => saveApiKey(apiKey),
    onSuccess: (result: ApiKeyStatus) => {
      queryClient.setQueryData(settingsKeys.apiKey(), result)
      setApiKey('')
      setState('saved')
    },
    onError: fail,
  })
  const remove = useMutation({
    mutationFn: removeApiKey,
    onSuccess: () => {
      queryClient.setQueryData<ApiKeyStatus>(settingsKeys.apiKey(), { configured: false, lastFour: null, rejected: false })
      setState('idle')
      setError('')
    },
    onError: fail,
  })
  // Reappears whenever no working key is on file: none was ever saved, or the one saved was refused.
  const needsInput = !data?.configured || data.rejected
  return (
    <Card title={t('apiKey.title')}>
      <div className="flex flex-col gap-3">
        <p className="text-mu text-[11.5px] leading-relaxed">{t('apiKey.description')}</p>
        {data?.configured ? (
          <div className="flex flex-wrap items-center gap-3">
            <span className="text-tx text-[12.5px]">{t('apiKey.configuredStatus', { lastFour: data.lastFour })}</span>
            <Button size="sm" variant="secondary" onClick={() => remove.mutate()} loading={remove.isPending}>
              {t('apiKey.remove')}
            </Button>
          </div>
        ) : null}
        {data?.rejected ? <Alert tone="error">{t('apiKey.rejectedNotice')}</Alert> : null}
        {needsInput ? (
          <div className="flex flex-col gap-3">
            <TextField
              label={t('apiKey.inputLabel')}
              type="password"
              autoComplete="off"
              value={apiKey}
              onChange={(event) => {
                setApiKey(event.target.value)
                setState('idle')
              }}
            />
            <div className="flex flex-wrap items-center gap-3">
              <Button
                size="sm"
                className="sm:max-w-[140px]"
                onClick={() => {
                  setState('saving')
                  save.mutate()
                }}
                disabled={state === 'saving' || apiKey.trim().length === 0}
                loading={state === 'saving'}
              >
                {state === 'saving' ? t('common:actions.saving') : t('apiKey.save')}
              </Button>
              {state === 'saved' ? <span role="status" className="text-up text-[12.5px]">{t('common:actions.saved')}</span> : null}
            </div>
          </div>
        ) : null}
        {state === 'error' ? <Alert tone="error">{error}</Alert> : null}
      </div>
    </Card>
  )
}
